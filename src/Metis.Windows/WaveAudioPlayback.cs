using System.IO;
using Metis.Core.Contracts;
using Metis.Core.Models;
using NAudio.Wave;

namespace Metis.Windows;

public sealed class WaveAudioPlayback : IAudioPlayback
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private RawSourceWaveStream? _source;
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
    /// Metis was in the middle of speaking. Worse, the speaking caller was
    /// handed a completed task and had no way to tell playback had been cut
    /// off, so it logged nothing: the voice simply went quiet for no visible
    /// reason. A cue now yields to speech instead.
    ///
    /// The whole decision happens under the lock so a cue arriving between the
    /// check and the first sample cannot slip through. The displaced playback
    /// is torn down afterwards, outside the lock, because WaveOutEvent.Stop
    /// waits on the playback thread and that thread may already be blocked on
    /// this same lock inside the stopped handler.
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

            // The old device is released before the new one is opened. Opening
            // a second one first left the incoming audio waiting behind the
            // audio it was supposed to replace, so a spoken error arriving over
            // a cue was delayed by the whole remaining length of that cue.
            Stop();

            lock (_gate)
            {
                _stream = new MemoryStream(audio.PcmData, false);
                _source = new RawSourceWaveStream(
                    _stream,
                    new WaveFormat(audio.SampleRate, audio.BitsPerSample, audio.Channels));
                _output = new WaveOutEvent();
                _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _priority = priority;
                _output.PlaybackStopped += Output_OnPlaybackStopped;
                _output.Init(_source);
                _output.Play();
                completionTask = _completion.Task;
            }
        }
        finally
        {
            _setup.Release();
        }

        using var registration = cancellationToken.Register(Stop);
        await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        WaveOutEvent? output;
        RawSourceWaveStream? source;
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
            output.PlaybackStopped -= Output_OnPlaybackStopped;
            output.Stop();
            output.Dispose();
        }

        source?.Dispose();
        stream?.Dispose();
        completion?.TrySetResult();
    }

    private void Output_OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        TaskCompletionSource? completion;
        WaveOutEvent? output;
        RawSourceWaveStream? source;
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
            output.PlaybackStopped -= Output_OnPlaybackStopped;
            output.Dispose();
        }

        source?.Dispose();
        stream?.Dispose();

        if (eventArgs.Exception is not null)
        {
            completion?.TrySetException(new InvalidOperationException("Windows could not play Metis's speech audio.", eventArgs.Exception));
        }
        else
        {
            completion?.TrySetResult();
        }
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
