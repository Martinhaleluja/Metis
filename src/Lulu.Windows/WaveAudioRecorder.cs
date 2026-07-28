using System.Diagnostics;
using System.IO;
using Lulu.Core.Contracts;
using Lulu.Core.Models;
using NAudio.Wave;

namespace Lulu.Windows;

public sealed class WaveAudioRecorder : IAudioRecorder
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _buffer;
    private TaskCompletionSource<RecordedAudio?>? _completion;
    private Stopwatch? _duration;
    private string _deviceName = "Default microphone";
    private bool _discard;
    private bool _disposed;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _waveIn is not null;
            }
        }
    }

    public event EventHandler<float>? LevelChanged;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var devices = new List<AudioDeviceInfo>();
        for (var index = 0; index < WaveIn.DeviceCount; index++)
        {
            var capabilities = WaveIn.GetCapabilities(index);
            devices.Add(new AudioDeviceInfo(index.ToString(), capabilities.ProductName));
        }

        return devices;
    }

    public void Start(string? preferredDeviceId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_waveIn is not null)
            {
                return;
            }

            if (WaveIn.DeviceCount == 0)
            {
                throw new InvalidOperationException("Windows reports no microphone input device. Connect or enable a microphone and try again.");
            }

            var deviceNumber = ResolveDevice(preferredDeviceId);
            var capabilities = WaveIn.GetCapabilities(deviceNumber);
            _deviceName = capabilities.ProductName;
            _buffer = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
                NumberOfBuffers = 3
            };
            _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
            _completion = new TaskCompletionSource<RecordedAudio?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _duration = Stopwatch.StartNew();
            _discard = false;
            _waveIn.DataAvailable += WaveIn_OnDataAvailable;
            _waveIn.RecordingStopped += WaveIn_OnRecordingStopped;

            try
            {
                _waveIn.StartRecording();
            }
            catch
            {
                CleanupLocked();
                throw;
            }
        }
    }

    public async Task<RecordedAudio?> StopAsync(CancellationToken cancellationToken = default)
    {
        WaveInEvent? waveIn;
        Task<RecordedAudio?>? completion;
        lock (_gate)
        {
            waveIn = _waveIn;
            completion = _completion?.Task;
        }

        if (waveIn is null || completion is null)
        {
            return null;
        }

        waveIn.StopRecording();
        return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Cancel()
    {
        WaveInEvent? waveIn;
        lock (_gate)
        {
            _discard = true;
            waveIn = _waveIn;
        }

        waveIn?.StopRecording();
    }

    private void WaveIn_OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        float level;
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            _writer.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            level = CalculateLevel(eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded));
        }

        LevelChanged?.Invoke(this, level);
    }

    private void WaveIn_OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        TaskCompletionSource<RecordedAudio?>? completion;
        RecordedAudio? result = null;
        lock (_gate)
        {
            completion = _completion;
            _duration?.Stop();
            _writer?.Dispose();
            _writer = null;

            if (!_discard && eventArgs.Exception is null && _buffer is not null && _duration is not null)
            {
                result = new RecordedAudio(_buffer.ToArray(), _duration.Elapsed, _deviceName);
            }

            CleanupLocked();
        }

        LevelChanged?.Invoke(this, 0);
        if (eventArgs.Exception is not null)
        {
            completion?.TrySetException(new InvalidOperationException(
                "Windows stopped the microphone unexpectedly. Check the device and privacy permission.",
                eventArgs.Exception));
        }
        else
        {
            completion?.TrySetResult(result);
        }
    }

    private void CleanupLocked()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= WaveIn_OnDataAvailable;
            _waveIn.RecordingStopped -= WaveIn_OnRecordingStopped;
            _waveIn.Dispose();
        }

        _waveIn = null;
        _writer?.Dispose();
        _writer = null;
        _buffer?.Dispose();
        _buffer = null;
        _completion = null;
        _duration = null;
        _discard = false;
    }

    private static int ResolveDevice(string? preferredDeviceId)
    {
        if (int.TryParse(preferredDeviceId, out var index) && index >= 0 && index < WaveIn.DeviceCount)
        {
            return index;
        }

        return 0;
    }

    private static float CalculateLevel(ReadOnlySpan<byte> audio)
    {
        if (audio.Length < 2)
        {
            return 0;
        }

        double sumSquares = 0;
        var sampleCount = audio.Length / 2;
        for (var offset = 0; offset + 1 < audio.Length; offset += 2)
        {
            var sample = (short)(audio[offset] | (audio[offset + 1] << 8));
            var normalized = sample / 32768d;
            sumSquares += normalized * normalized;
        }

        var rootMeanSquare = Math.Sqrt(sumSquares / sampleCount);
        return (float)Math.Clamp(rootMeanSquare * 4.5, 0, 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cancel();
        lock (_gate)
        {
            CleanupLocked();
            _disposed = true;
        }
    }
}
