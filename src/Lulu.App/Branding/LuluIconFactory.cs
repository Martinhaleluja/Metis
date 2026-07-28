using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Lulu.App.Branding;

internal static class LuluIconFactory
{
    public static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(255, 120, 216, 255)))
        using (var path = CreateCompanionPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillPath(brush, path);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateCompanionPath()
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(34, 8, 42, -1, 56, -1, 64, 7);
        path.AddBezier(64, 7, 72, 15, 72, 27, 66, 35);
        path.AddBezier(66, 35, 72, 43, 72, 55, 64, 63);
        path.AddBezier(64, 63, 56, 71, 44, 71, 35, 65);
        path.AddBezier(35, 65, 27, 71, 15, 71, 7, 63);
        path.AddBezier(7, 63, -1, 55, -1, 43, 5, 35);
        path.AddBezier(5, 35, -1, 27, -1, 15, 7, 7);
        path.AddBezier(7, 7, 15, -1, 27, -1, 34, 8);
        path.CloseFigure();
        using var transform = new Matrix(0.4f, 0, 0, 0.4f, 1.6f, 1.6f);
        path.Transform(transform);
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
