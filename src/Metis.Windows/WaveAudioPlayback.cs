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
    private bool _disposed;

    public async Task PlayAsync(SpeechAudio audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (audio.PcmData.Length == 0)
        {
            return;
        }

        Stop();
        Task completionTask;
        lock (_gate)
        {
            _stream = new MemoryStream(audio.PcmData, false);
            _source = new RawSourceWaveStream(
                _stream,
                new WaveFormat(audio.SampleRate, audio.BitsPerSample, audio.Channels));
            _output = new WaveOutEvent();
            _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _output.PlaybackStopped += Output_OnPlaybackStopped;
            _output.Init(_source);
            _output.Play();
            completionTask = _completion.Task;
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
