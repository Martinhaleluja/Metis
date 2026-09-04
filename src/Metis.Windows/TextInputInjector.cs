using System.Runtime.InteropServices;

namespace Metis.Windows;

/// <summary>
/// Universal text injection helper that inserts text into the active focused control
/// via the clipboard and synthesized paste shortcut (Ctrl+V), while preserving the user's
/// prior clipboard contents.
/// </summary>
public static class TextInputInjector
{
    private const uint CfUnicodetext = 13;
    private const uint GmemMoveable = 0x0002;
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventFKeyup = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    /// <summary>
    /// Inserts the specified text into the active control by placing it on the clipboard,
    /// synthesizing Ctrl+V, and restoring the prior clipboard content.
    /// </summary>
    public static async Task<bool> InsertTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string? previousClipboardText = null;
        try
        {
            // 1. Backup previous text if available
            previousClipboardText = GetCurrentClipboardText();

            // 2. Set new text to clipboard
            if (!SetClipboardText(text))
            {
                // Fallback to STA thread if direct Win32 failed
                SetClipboardTextSta(text);
            }

            // 3. Allow target application a moment to register clipboard change
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);

            // 4. Synthesize Ctrl+V
            SynthesizePaste();

            // 5. Allow target application to process the paste message before restoring clipboard
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);

            // 6. Restore original clipboard text if there was any
            if (previousClipboardText is not null)
            {
                SetClipboardText(previousClipboardText);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SynthesizePaste()
    {
        // Ctrl down, V down, V up, Ctrl up
        keybd_event(VkControl, 0, 0, 0);
        keybd_event(VkV, 0, 0, 0);
        keybd_event(VkV, 0, KeyEventFKeyup, 0);
        keybd_event(VkControl, 0, KeyEventFKeyup, 0);
    }

    private static string? GetCurrentClipboardText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(nint.Zero))
            {
                try
                {
                    if (IsClipboardFormatAvailable(CfUnicodetext))
                    {
                        var handle = GetClipboardData(CfUnicodetext);
                        if (handle != nint.Zero)
                        {
                            var pointer = GlobalLock(handle);
                            if (pointer != nint.Zero)
                            {
                                try
                                {
                                    return Marshal.PtrToStringUni(pointer);
                                }
                                finally
                                {
                                    GlobalUnlock(handle);
                                }
                            }
                        }
                    }
                    return null;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(10);
        }
        return null;
    }

    private static bool SetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(nint.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytesNeeded = (nuint)((text.Length + 1) * 2);
                    var hGlobal = GlobalAlloc(GmemMoveable, bytesNeeded);
                    if (hGlobal == nint.Zero)
                    {
                        return false;
                    }

                    var targetPointer = GlobalLock(hGlobal);
                    if (targetPointer == nint.Zero)
                    {
                        GlobalFree(hGlobal);
                        return false;
                    }

                    try
                    {
                        var chars = text.ToCharArray();
                        Marshal.Copy(chars, 0, targetPointer, chars.Length);
                        Marshal.WriteInt16(targetPointer + (chars.Length * 2), 0); // null-terminator
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }

                    if (SetClipboardData(CfUnicodetext, hGlobal) == nint.Zero)
                    {
                        GlobalFree(hGlobal);
                        return false;
                    }

                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(10);
        }

        return false;
    }

    private static void SetClipboardTextSta(string text)
    {
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
            }
            catch
            {
                // Ignore STA clipboard errors
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(500);
    }
}
