using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Metis.Windows;

/// <summary>
/// Detects whether the currently focused control in the foreground window accepts text input.
/// Uses fast Win32 caret and class name checks followed by a safe UI Automation fallback.
/// </summary>
public static class EditableInputDetector
{
    private static readonly Lazy<UIA3Automation> Automation = new(
        () => new UIA3Automation(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Returns true if the user's cursor is currently focused in an editable text field.
    /// </summary>
    public static bool IsFocusedElementEditable()
    {
        try
        {
            var foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd == nint.Zero)
            {
                return false;
            }

            var threadId = GetWindowThreadProcessId(foregroundHwnd, out _);
            if (threadId != 0)
            {
                var guiInfo = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                if (GetGUIThreadInfo(threadId, ref guiInfo))
                {
                    // 1. Active native caret check (fastest, covers Notepad, Word, browser inputs, edit controls)
                    if (guiInfo.hwndCaret != nint.Zero)
                    {
                        return true;
                    }

                    if (guiInfo.rcCaret.Right > guiInfo.rcCaret.Left)
                    {
                        return true;
                    }

                    // 2. Focused window class name check
                    var focusHwnd = guiInfo.hwndFocus != nint.Zero ? guiInfo.hwndFocus : foregroundHwnd;
                    var classNameBuilder = new StringBuilder(256);
                    if (GetClassName(focusHwnd, classNameBuilder, classNameBuilder.Capacity) > 0)
                    {
                        var className = classNameBuilder.ToString();
                        if (IsKnownEditableClassName(className))
                        {
                            return true;
                        }
                    }
                }
            }

            // 3. Fallback to UI Automation for modern apps (Electron, WPF, UWP, Chrome/Edge without Win32 caret)
            return CheckViaAutomation();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownEditableClassName(string className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        return className.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("TextBox", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Scintilla", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("TermContainerClass", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckViaAutomation()
    {
        try
        {
            var task = Task.Run(() =>
            {
                var focused = Automation.Value.FocusedElement();
                if (focused is null)
                {
                    return false;
                }

                var controlType = focused.ControlType;
                if (controlType == ControlType.Edit || controlType == ControlType.Document)
                {
                    return true;
                }

                var patterns = focused.Patterns;
                if (patterns.Value.IsSupported && !patterns.Value.Pattern.IsReadOnly.Value)
                {
                    return true;
                }

                if (patterns.Text.IsSupported)
                {
                    return true;
                }

                return false;
            });

            // Guard against unresponsive UI thread blocking with a 200ms timeout
            if (task.Wait(TimeSpan.FromMilliseconds(200)))
            {
                return task.Result;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
