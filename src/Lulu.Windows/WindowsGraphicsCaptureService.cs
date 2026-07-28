using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Lulu.Core.Contracts;
using Lulu.Core.Models;
using WinRT;

namespace Lulu.Windows;

/// <summary>
/// Captures the foreground HWND through Windows.Graphics.Capture and a D3D11
/// frame pool. The proven GDI implementation remains a compatibility fallback.
/// </summary>
public sealed class WindowsGraphicsCaptureService : IScreenCaptureService
{
    private const int DwmExtendedFrameBounds = 9;
    private const int MaximumWidth = 1600;
    private const int MaximumHeight = 1000;
    private const float JpegQuality = 0.82f;
    private const string CaptureMimeType = "image/jpeg";
    private readonly IScreenCaptureService _fallback;

    public WindowsGraphicsCaptureService(IScreenCaptureService? fallback = null)
    {
        _fallback = fallback ?? new ActiveWindowCaptureService();
    }

    public async Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default)
    {
        var window = CaptureWindowSelector.GetTargetWindow();
        if (window == nint.Zero || IsIconic(window))
        {
            return null;
        }

        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                return await _fallback.CaptureActiveWindowAsync(cancellationToken).ConfigureAwait(false);
            }

            var bounds = GetBounds(window);
            var sourceWidth = bounds.Right - bounds.Left;
            var sourceHeight = bounds.Bottom - bounds.Top;
            if (sourceWidth <= 1 || sourceHeight <= 1)
            {
                return null;
            }

            var scale = Math.Min(
                1d,
                Math.Min(MaximumWidth / (double)sourceWidth, MaximumHeight / (double)sourceHeight));
            var outputWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var outputHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var imageBytes = await CaptureWindowJpegAsync(
                window,
                outputWidth,
                outputHeight,
                cancellationToken).ConfigureAwait(false);
            return new ScreenCapture(
                imageBytes,
                GetWindowTitle(window),
                outputWidth,
                outputHeight,
                bounds.Left,
                bounds.Top,
                sourceWidth,
                sourceHeight,
                window.ToInt64(),
                "Windows.Graphics.Capture (D3D11)",
                CaptureMimeType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await _fallback.CaptureActiveWindowAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> CaptureWindowJpegAsync(
        nint window,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        var item = CreateItemForWindow(window);
        var device = CreateDirect3DDevice();
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
        try
        {
            session.IsCursorCaptureEnabled = false;
        }
        catch
        {
            // Older supported builds may not expose this optional setting.
        }

        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processingFrame = 0;
        TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
        handler = async (sender, _) =>
        {
            if (Interlocked.Exchange(ref processingFrame, 1) != 0)
            {
                return;
            }

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    Interlocked.Exchange(ref processingFrame, 0);
                    return;
                }

                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    frame.Surface,
                    BitmapAlphaMode.Ignore);
                using var stream = new InMemoryRandomAccessStream();
                var encoderOptions = new BitmapPropertySet
                {
                    ["ImageQuality"] = new BitmapTypedValue(JpegQuality, PropertyType.Single)
                };
                var encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.JpegEncoderId,
                    stream,
                    encoderOptions);
                encoder.SetSoftwareBitmap(bitmap);
                encoder.BitmapTransform.ScaledWidth = (uint)outputWidth;
                encoder.BitmapTransform.ScaledHeight = (uint)outputHeight;
                await encoder.FlushAsync();

                if (stream.Size > int.MaxValue)
                {
                    throw new InvalidOperationException("The captured frame was too large to encode safely.");
                }

                var bytes = new byte[(int)stream.Size];
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                completion.TrySetResult(bytes);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        };

        framePool.FrameArrived += handler;
        session.StartCapture();
        try
        {
            return await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= handler;
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint window)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForWindow(window, GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        var result = D3D11CreateDevice(
            nint.Zero,
            D3dDriverTypeHardware,
            nint.Zero,
            D3d11CreateDeviceBgraSupport,
            nint.Zero,
            0,
            D3d11SdkVersion,
            out var nativeDevice,
            out _,
            out var nativeContext);
        if (result < 0)
        {
            result = D3D11CreateDevice(
                nint.Zero,
                D3dDriverTypeWarp,
                nint.Zero,
                D3d11CreateDeviceBgraSupport,
                nint.Zero,
                0,
                D3d11SdkVersion,
                out nativeDevice,
                out _,
                out nativeContext);
        }

        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        nint dxgiDevice = nint.Zero;
        nint inspectableDevice = nint.Zero;
        try
        {
            var iid = IdxgiDeviceGuid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativeDevice, ref iid, out dxgiDevice));
            var createResult = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectableDevice);
            if (createResult < 0)
            {
                Marshal.ThrowExceptionForHR(createResult);
            }

            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
        }
        finally
        {
            if (inspectableDevice != nint.Zero)
            {
                Marshal.Release(inspectableDevice);
            }

            if (dxgiDevice != nint.Zero)
            {
                Marshal.Release(dxgiDevice);
            }

            if (nativeContext != nint.Zero)
            {
                Marshal.Release(nativeContext);
            }

            if (nativeDevice != nint.Zero)
            {
                Marshal.Release(nativeDevice);
            }
        }
    }

    private static NativeRect GetBounds(nint window)
    {
        if (DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(window, out bounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not measure the active window.");
        }

        return bounds;
    }

    private static string GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return "Untitled active window";
        }

        var builder = new System.Text.StringBuilder(length + 1);
        return GetWindowText(window, builder, builder.Capacity) > 0
            ? builder.ToString()
            : "Untitled active window";
    }

    private const int D3dDriverTypeHardware = 1;
    private const int D3dDriverTypeWarp = 5;
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private const uint D3d11SdkVersion = 7;
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IdxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([In] nint window, in Guid iid);
        nint CreateForMonitor([In] nint monitor, in Guid iid);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out nint device,
        out uint featureLevel,
        out nint immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out NativeRect value,
        int valueSize);
}
