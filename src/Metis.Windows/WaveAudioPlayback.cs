using System.IO;
using Metis.Core.Contracts;
using Metis.Core.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Metis.Windows;

public sealed class WaveAudioPlayback : IAudioPlayback
{
    private readonly object _gate = new();
    private IWavePlayer? _output;
    private IDisposable? _source;
    private MemoryStream? _stream;
    private TaskCompletionSource? _completion;

    /// <summary>What is playing right now, so a cue cannot displace speech.</summary>
    private AudioPriority _priority = AudioPriority.Cue;

    /// <summary>
    /// Serialises the decide-stop-start sequence below. It cannot be done under
    /// _gate, because stopping waits on the playback thread and that thread may
    /// be inside the stopped handler waiting for _gate.
    /// </summary>
    private readonly SemaphoreSlim _setup = new(1, 1);

    private bool _disposed;

    /// <summary>
    /// Starting playback takes the one output device, so whatever was playing
    /// stops. That used to be unconditional, which meant any cue — a keypress,
    /// a saved setting, a finished task — could silently truncate a sentence
    /// Metis was in the middle of speaking. A cue now yields to speech instead.
    ///
    /// Plays raw PCM (16-bit, 24kHz/16kHz mono), WAV audio with RIFF headers,
    /// and MP3 audio streams seamlessly. Handles device disconnection, format
    /// mismatch, and buffer underflows without crashing.
    /// </summary>
    public async Task PlayAsync(
        SpeechAudio audio,
        AudioPriority priority = AudioPriority.Speech,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (audio.PcmData.Length == 0)
        {
            return;
        }

        Task completionTask;
        await _setup.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (AudioArbitration.ShouldDrop(priority, _priority, _output is not null))
                {
                    return;
                }
            }

            // The old device is released before the new one is opened.
            Stop();

            lock (_gate)
            {
                try
                {
                    var (output, source, stream) = CreateAudioPipeline(audio);
                    _stream = stream;
                    _source = source;
                    _output = output;
                    _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _priority = priority;

                    _output.PlaybackStopped += Output_OnPlaybackStopped;
                    _output.Play();
                    completionTask = _completion.Task;
                }
                catch
                {
                    ClearCurrentLocked();
                    return;
                }
            }
        }
        finally
        {
            _setup.Release();
        }

        using var registration = cancellationToken.Register(Stop);
        try
        {
            await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Stop();
            throw;
        }
        catch
        {
            // Suppress device errors during playback so callers never crash
        }
    }

    public void Stop()
    {
        IWavePlayer? output;
        IDisposable? source;
        MemoryStream? stream;
        TaskCompletionSource? completion;
        lock (_gate)
        {
            output = _output;
            source = _source;
            stream = _stream;
            completion = _completion;
            ClearCurrentLocked();
        }

        if (output is not null)
        {
            try
            {
                output.PlaybackStopped -= Output_OnPlaybackStopped;
                output.Stop();
            }
            catch
            {
            }
            finally
            {
                output.Dispose();
            }
        }

        source?.Dispose();
        stream?.Dispose();
        completion?.TrySetResult();
    }

    private void Output_OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        TaskCompletionSource? completion;
        IWavePlayer? output;
        IDisposable? source;
        MemoryStream? stream;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            completion = _completion;
            output = _output;
            source = _source;
            stream = _stream;
            ClearCurrentLocked();
        }

        if (output is not null)
        {
            try
            {
                output.PlaybackStopped -= Output_OnPlaybackStopped;
            }
            catch
            {
            }
            finally
            {
                output.Dispose();
            }
        }

        source?.Dispose();
        stream?.Dispose();

        // Gracefully complete playback without crashing on device disconnect or buffer underflow
        completion?.TrySetResult();
    }

    private static (IWavePlayer output, IDisposable source, MemoryStream stream) CreateAudioPipeline(SpeechAudio audio)
    {
        var stream = new MemoryStream(audio.PcmData, false);
        IWaveProvider waveProvider;
        IDisposable sourceDisposable;

        try
        {
            if (IsRiffWave(audio.PcmData))
            {
                var waveFileReader = new WaveFileReader(stream);
                sourceDisposable = waveFileReader;
                waveProvider = waveFileReader.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                    ? new SampleToWaveProvider16(waveFileReader.ToSampleProvider())
                    : waveFileReader;
            }
            else if (IsMp3(audio.PcmData, audio.MimeType))
            {
                var mp3Reader = new Mp3FileReader(stream);
                sourceDisposable = mp3Reader;
                waveProvider = mp3Reader;
            }
            else
            {
                var sampleRate = audio.SampleRate > 0 ? audio.SampleRate : 24000;
                var bitsPerSample = audio.BitsPerSample > 0 ? audio.BitsPerSample : 16;
                var channels = audio.Channels > 0 ? audio.Channels : 1;
                var rawSource = new RawSourceWaveStream(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
                sourceDisposable = rawSource;
                waveProvider = rawSource;
            }
        }
        catch
        {
            stream.Position = 0;
            var sampleRate = audio.SampleRate > 0 ? audio.SampleRate : 24000;
            var bitsPerSample = audio.BitsPerSample > 0 ? audio.BitsPerSample : 16;
            var channels = audio.Channels > 0 ? audio.Channels : 1;
            var rawSource = new RawSourceWaveStream(stream, new WaveFormat(sampleRate, bitsPerSample, channels));
            sourceDisposable = rawSource;
            waveProvider = rawSource;
        }

        IWavePlayer player;
        try
        {
            var waveOut = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 3
            };
            waveOut.Init(waveProvider);
            player = waveOut;
        }
        catch
        {
            try
            {
                // Fallback to WasapiOut if WaveOutEvent fails
                var wasapiOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                wasapiOut.Init(waveProvider);
                player = wasapiOut;
            }
            catch
            {
                // Fallback standard WaveOutEvent
                var waveOut = new WaveOutEvent();
                waveOut.Init(waveProvider);
                player = waveOut;
            }
        }

        return (player, sourceDisposable, stream);
    }

    private static bool IsRiffWave(byte[] data) =>
        data.Length >= 12 &&
        data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && // "RIFF"
        data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45; // "WAVE"

    private static bool IsMp3(byte[] data, string? mimeType)
    {
        if (!string.IsNullOrWhiteSpace(mimeType) &&
            (mimeType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ||
             mimeType.Contains("mp3", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (data.Length >= 3 && data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) // "ID3"
        {
            return true;
        }

        return data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0;
    }

    private void ClearCurrentLocked()
    {
        _output = null;
        _source = null;
        _stream = null;
        _completion = null;

        // Silence is the lowest priority: with nothing playing, the next cue
        // has nothing to yield to.
        _priority = AudioPriority.Cue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _setup.Dispose();
        _disposed = true;
    }
}
