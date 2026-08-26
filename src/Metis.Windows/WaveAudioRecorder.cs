using System.Diagnostics;
using System.IO;
using Metis.Core.Contracts;
using Metis.Core.Models;
using NAudio.Wave;

namespace Metis.Windows;

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
        var deviceCount = WaveIn.DeviceCount;
        for (var index = 0; index < deviceCount; index++)
        {
            try
            {
                var capabilities = WaveIn.GetCapabilities(index);
                devices.Add(new AudioDeviceInfo(index.ToString(), capabilities.ProductName));
            }
            catch
            {
                devices.Add(new AudioDeviceInfo(index.ToString(), $"Microphone {index}"));
            }
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
            try
            {
                var capabilities = WaveIn.GetCapabilities(deviceNumber);
                _deviceName = capabilities.ProductName;
            }
            catch
            {
                _deviceName = "Default microphone";
            }

            _buffer = new MemoryStream();
            _duration = Stopwatch.StartNew();
            _discard = false;
            _completion = new TaskCompletionSource<RecordedAudio?>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Attempt 16kHz mono 16-bit first (standard for speech recognition), fallback to 44.1kHz or 48kHz
            WaveInEvent? waveIn = null;
            WaveFileWriter? writer = null;
            var sampleRates = new[] { 16000, 44100, 48000, 22050 };

            foreach (var rate in sampleRates)
            {
                try
                {
                    var testWaveIn = new WaveInEvent
                    {
                        DeviceNumber = deviceNumber,
                        WaveFormat = new WaveFormat(rate, 16, 1),
                        BufferMilliseconds = 50,
                        NumberOfBuffers = 3
                    };
                    var testWriter = new WaveFileWriter(new IgnoreDisposeStream(_buffer), testWaveIn.WaveFormat);
                    testWaveIn.DataAvailable += WaveIn_OnDataAvailable;
                    testWaveIn.RecordingStopped += WaveIn_OnRecordingStopped;
                    testWaveIn.StartRecording();

                    waveIn = testWaveIn;
                    writer = testWriter;
                    break;
                }
                catch
                {
                    writer?.Dispose();
                    waveIn?.Dispose();
                    writer = null;
                    waveIn = null;
                }
            }

            if (waveIn is null || writer is null)
            {
                CleanupLocked();
                throw new InvalidOperationException("Windows could not open the microphone audio stream. Check audio device settings.");
            }

            _waveIn = waveIn;
            _writer = writer;
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

        try
        {
            waveIn.StopRecording();
        }
        catch
        {
        }

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

        try
        {
            waveIn?.StopRecording();
        }
        catch
        {
        }
    }

    private void WaveIn_OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            float level;
            lock (_gate)
            {
                if (_writer is null || eventArgs.BytesRecorded <= 0)
                {
                    return;
                }

                try
                {
                    _writer.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                }
                catch
                {
                    return;
                }

                level = CalculateLevel(eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded));
            }

            LevelChanged?.Invoke(this, level);
        }
        catch
        {
            // Protect background callback thread from unhandled exceptions
        }
    }

    private void WaveIn_OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        try
        {
            TaskCompletionSource<RecordedAudio?>? completion;
            RecordedAudio? result = null;
            lock (_gate)
            {
                completion = _completion;
                _duration?.Stop();

                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch
                {
                }
                _writer = null;

                if (!_discard && _buffer is not null && _duration is not null && _buffer.Length > 44)
                {
                    result = new RecordedAudio(_buffer.ToArray(), _duration.Elapsed, _deviceName);
                }

                CleanupLocked();
            }

            try
            {
                LevelChanged?.Invoke(this, 0);
            }
            catch
            {
            }

            if (eventArgs.Exception is not null && result is null)
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
        catch (Exception exception)
        {
            // Safeguard background callback from unhandled crashes
            try
            {
                _completion?.TrySetException(exception);
            }
            catch
            {
            }
        }
    }

    private void CleanupLocked()
    {
        if (_waveIn is not null)
        {
            try
            {
                _waveIn.DataAvailable -= WaveIn_OnDataAvailable;
                _waveIn.RecordingStopped -= WaveIn_OnRecordingStopped;
                _waveIn.Dispose();
            }
            catch
            {
            }
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

    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;

        public IgnoreDisposeStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // Do not dispose the inner stream
        }
    }
}
