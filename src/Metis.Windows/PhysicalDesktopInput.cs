using System.Diagnostics;
using System.Runtime.InteropServices;
using Metis.Core.Models;

namespace Metis.Windows;

internal interface IPhysicalDesktopInput
{
    bool TryMoveAt(int screenX, int screenY, out int error);
    bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error);
    bool TryTypeText(string text, out int error);
    bool TryPressKey(string key, out int error);
    bool TryOpenApp(string appName, out int error);
    bool TryOpenUrl(string url, out int error);
    bool TryRunCommand(string command, bool elevated, out int error);
}

/// <summary>
/// Strong input fallback for controls that reject UI Automation and background
/// window messages, including the Windows taskbar and many modern UI surfaces.
/// The original pointer position is restored after five seconds unless the user
/// has moved it themselves in the meantime.
/// </summary>
internal sealed class NativePhysicalDesktopInput : IPhysicalDesktopInput
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkPrior = 0x21;
    private const ushort VkNext = 0x22;
    private const ushort VkEnd = 0x23;
    private const ushort VkHome = 0x24;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkDelete = 0x2E;
    private const ushort VkLWin = 0x5B;
    private const ushort VkF1 = 0x70;
    private readonly object _restoreLock = new();
    private CancellationTokenSource? _pendingRestore;

    public bool TryMoveAt(int screenX, int screenY, out int error)
    {
        if (!TryMoveNative(screenX, screenY, out var original, out error))
        {
            return false;
        }

        ScheduleRestore(original, new NativePoint(screenX, screenY));
        return true;
    }

    public bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error)
    {
        if (kind is not (DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick))
        {
            error = 87;
            return false;
        }

        if (!TryMoveNative(screenX, screenY, out var original, out error))
        {
            return false;
        }

        var inputs = CreateClickInputs(kind);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            error = Marshal.GetLastWin32Error();
            _ = SetCursorPos(original.X, original.Y);
            return false;
        }

        error = 0;
        ScheduleRestore(original, new NativePoint(screenX, screenY));
        return true;
    }

    public bool TryTypeText(string text, out int error)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 4_000 ||
            text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            error = 87;
            return false;
        }

        var inputs = new List<NativeInput>(256);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                continue;
            }

            if (character is '\r' or '\n' or '\t')
            {
                var virtualKey = character == '\t' ? VkTab : VkReturn;
                inputs.Add(VirtualKeyInput(virtualKey, false, false));
                inputs.Add(VirtualKeyInput(virtualKey, true, false));
            }
            else
            {
                inputs.Add(UnicodeInput(character, false));
                inputs.Add(UnicodeInput(character, true));
            }

            if (inputs.Count < 256 && index < text.Length - 1)
            {
                continue;
            }

            if (!TrySend(inputs.ToArray(), out error))
            {
                return false;
            }

            inputs.Clear();
            Thread.Sleep(4);
        }

        error = 0;
        return true;
    }

    public bool TryPressKey(string key, out int error)
    {
        if (!TryParseKeyChord(key, out var modifiers, out var keyCode, out var extended))
        {
            error = 87;
            return false;
        }

        var inputs = new List<NativeInput>(modifiers.Count * 2 + 2);
        inputs.AddRange(modifiers.Select(modifier => VirtualKeyInput(modifier, false, false)));
        inputs.Add(VirtualKeyInput(keyCode, false, extended));
        inputs.Add(VirtualKeyInput(keyCode, true, extended));
        for (var index = modifiers.Count - 1; index >= 0; index--)
        {
            inputs.Add(VirtualKeyInput(modifiers[index], true, false));
        }

        return TrySend(inputs.ToArray(), out error);
    }

    /// <summary>
    /// How long the Start menu is given to take focus, and then how long
    /// Windows Search is given to resolve what was typed.
    ///
    /// The old values were 180 ms and 260 ms, which are shorter than the Start
    /// menu takes to appear on an ordinary machine. Pressing Enter early does
    /// not merely fail: the search list is still settling, so the highlighted
    /// result may be something else entirely, and Metis launches the wrong
    /// program while reporting success. Waiting is the cheap half of that
    /// trade.
    /// </summary>
    private const int StartMenuFocusMilliseconds = 700;
    private const int SearchResolveMilliseconds = 1100;

    /// <summary>
    /// Opens any installed program by name.
    ///
    /// Two routes, in order of reliability. A direct shell launch resolves
    /// anything on PATH or carrying an App Paths registry entry — which covers
    /// the great majority of installed desktop software, from notepad and
    /// mspaint to Chrome and Code — and it does so with no dependence on the
    /// Start menu, no timing to get wrong, and no possibility of launching a
    /// neighbouring search result. Only when that fails does Metis fall back to
    /// typing into Windows Search, which is what reaches Store apps and things
    /// known by a display name rather than an executable.
    /// </summary>
    public bool TryOpenApp(string appName, out int error)
    {
        var normalized = appName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 100 || normalized.Any(char.IsControl))
        {
            error = 87;
            return false;
        }

        if (TryLaunchDirectly(normalized, out error))
        {
            return true;
        }

        if (!TryPressKey("win", out error))
        {
            return false;
        }

        Thread.Sleep(StartMenuFocusMilliseconds);
        if (!TryTypeText(normalized, out error))
        {
            return false;
        }

        Thread.Sleep(SearchResolveMilliseconds);
        return TryPressKey("enter", out error);
    }

    /// <summary>
    /// Characters that turn a program name into something else — a path, a
    /// second command, a redirect, a switch. A launch is given a bare name and
    /// never arguments, so anything carrying these is refused here and left to
    /// the search route, where it is typed as literal text rather than executed.
    /// </summary>
    private static readonly char[] NotInAProgramName =
        ['\\', '/', '"', '\'', '|', '&', ';', '<', '>', '%', '^', '`', '\t'];

    /// <summary>
    /// Whether a name is a bare program name that may be handed to the shell.
    ///
    /// A direct launch executes what it is given, so it is given only names.
    /// Anything carrying a path, a quote, a switch, or a second command is
    /// refused here and left to the search route, where it is typed as literal
    /// text into a search box rather than executed.
    /// </summary>
    internal static bool IsBareProgramName(string appName) =>
        !string.IsNullOrWhiteSpace(appName) &&
        appName.Length <= 100 &&
        !appName.Any(char.IsControl) &&
        appName.IndexOfAny(NotInAProgramName) < 0 &&
        !appName.StartsWith('-') &&
        !appName.Contains("..", StringComparison.Ordinal);

    private static bool TryLaunchDirectly(string appName, out int error)
    {
        error = 0;

        if (!IsBareProgramName(appName))
        {
            return false;
        }

        // ShellExecute resolves a bare name against PATH and the App Paths
        // registry. Appending .exe as a second attempt picks up the entries
        // registered with an extension, which is how most installers write them.
        foreach (var candidate in Candidates(appName))
        {
            try
            {
                using var started = Process.Start(new ProcessStartInfo(candidate)
                {
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                // Not resolvable under this name; try the next form, then fall
                // back to Windows Search.
            }
        }

        return false;
    }

    internal static IEnumerable<string> Candidates(string appName)
    {
        yield return appName;

        if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return appName + ".exe";

            // "Google Chrome" is registered as chrome.exe, "Visual Studio Code"
            // as code.exe: the last word is usually the executable's own name.
            var lastWord = appName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(lastWord) && !string.Equals(lastWord, appName, StringComparison.Ordinal))
            {
                yield return lastWord + ".exe";
            }
        }
    }

    /// <summary>
    /// Runs a reviewed system command through PowerShell, elevating only when
    /// the review said it was needed.
    ///
    /// The command is passed as a single argument to <c>-Command</c> rather
    /// than assembled into a longer line, and nothing the caller supplies is
    /// concatenated around it, so there is no second command to smuggle in
    /// through quoting. Elevation goes through ShellExecute's runas verb, which
    /// makes Windows show its own consent prompt — the user sees a UAC dialog
    /// naming PowerShell, on top of the confirmation Metis already asked for.
    /// </summary>
    public bool TryRunCommand(string command, bool elevated, out int error)
    {
        var review = Metis.Core.Services.SystemCommandPolicy.Review(command);
        if (review.IsRefused)
        {
            error = 87;
            return false;
        }

        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                CreateNoWindow = false
            };

            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(review.Command);

            if (elevated || review.NeedsElevation)
            {
                start.Verb = "runas";
            }

            using var started = Process.Start(start);
            error = 0;
            return started is not null;
        }
        catch
        {
            // A cancelled UAC prompt arrives here as an exception. That is the
            // user declining, which is a refusal rather than a fault.
            error = Marshal.GetLastWin32Error();
            return false;
        }
    }

    public bool TryOpenUrl(string url, out int error)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = 87;
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            error = 0;
            return true;
        }
        catch
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }
    }

    private static bool TryMoveNative(
        int screenX,
        int screenY,
        out NativePoint original,
        out int error)
    {
        original = default;
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0 ||
            screenX < left || screenX >= left + width ||
            screenY < top || screenY >= top + height)
        {
            error = 87;
            return false;
        }

        if (!GetCursorPos(out original) || !SetCursorPos(screenX, screenY))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        error = 0;
        return true;
    }

    private void ScheduleRestore(NativePoint original, NativePoint automationTarget)
    {
        CancellationTokenSource source;
        lock (_restoreLock)
        {
            _pendingRestore?.Cancel();
            _pendingRestore?.Dispose();
            source = new CancellationTokenSource();
            _pendingRestore = source;
        }

        _ = RestoreLaterAsync(original, automationTarget, source);
    }

    private async Task RestoreLaterAsync(
        NativePoint original,
        NativePoint automationTarget,
        CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), source.Token).ConfigureAwait(false);
            if (GetCursorPos(out var current) &&
                Math.Abs(current.X - automationTarget.X) <= 2 &&
                Math.Abs(current.Y - automationTarget.Y) <= 2)
            {
                _ = SetCursorPos(original.X, original.Y);
            }
        }
        catch (OperationCanceledException)
        {
            // A later action owns pointer restoration now.
        }
        finally
        {
            lock (_restoreLock)
            {
                if (ReferenceEquals(_pendingRestore, source))
                {
                    _pendingRestore = null;
                }
            }

            source.Dispose();
        }
    }

    private static NativeInput[] CreateClickInputs(DesktopActionKind kind)
    {
        if (kind == DesktopActionKind.RightClick)
        {
            return [MouseInput(MouseEventRightDown), MouseInput(MouseEventRightUp)];
        }

        var singleClick = new[] { MouseInput(MouseEventLeftDown), MouseInput(MouseEventLeftUp) };
        return kind == DesktopActionKind.DoubleClick
            ? [singleClick[0], singleClick[1], singleClick[0], singleClick[1]]
            : singleClick;
    }

    private static NativeInput MouseInput(uint flags) => new()
    {
        Type = InputMouse,
        Data = new NativeInputUnion { Mouse = new NativeMouseInput { Flags = flags } }
    };

    private static NativeInput UnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                Scan = character,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
            }
        }
    };

    private static NativeInput VirtualKeyInput(ushort keyCode, bool keyUp, bool extended) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = keyCode,
                Flags = (keyUp ? KeyEventKeyUp : 0) | (extended ? KeyEventExtendedKey : 0)
            }
        }
    };

    private static bool TrySend(NativeInput[] inputs, out int error)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent == (uint)inputs.Length)
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static bool TryParseKeyChord(
        string key,
        out List<ushort> modifiers,
        out ushort keyCode,
        out bool extended)
    {
        modifiers = [];
        keyCode = 0;
        extended = false;
        var parts = (key ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 4)
        {
            return false;
        }

        foreach (var modifier in parts[..^1])
        {
            var code = modifier switch
            {
                "ctrl" or "control" => VkControl,
                "shift" => VkShift,
                "alt" => VkMenu,
                "win" or "windows" => VkLWin,
                _ => (ushort)0
            };
            if (code == 0 || modifiers.Contains(code))
            {
                return false;
            }

            modifiers.Add(code);
        }

        var primary = parts[^1];
        keyCode = primary switch
        {
            "backspace" => VkBack,
            "tab" => VkTab,
            "enter" or "return" => VkReturn,
            "escape" or "esc" => VkEscape,
            "space" => VkSpace,
            "pageup" or "page_up" => VkPrior,
            "pagedown" or "page_down" => VkNext,
            "end" => VkEnd,
            "home" => VkHome,
            "left" => VkLeft,
            "up" => VkUp,
            "right" => VkRight,
            "down" => VkDown,
            "delete" or "del" => VkDelete,
            "ctrl" or "control" => VkControl,
            "shift" => VkShift,
            "alt" => VkMenu,
            "win" or "windows" => VkLWin,
            _ when primary.Length == 1 && char.IsLetterOrDigit(primary[0]) =>
                (ushort)char.ToUpperInvariant(primary[0]),
            _ when TryReadFunctionKey(primary, out var functionKey) => functionKey,
            _ => (ushort)0
        };
        extended = keyCode is VkPrior or VkNext or VkEnd or VkHome or VkLeft or VkUp or VkRight or VkDown or VkDelete or VkLWin;
        return keyCode != 0;
    }

    private static bool TryReadFunctionKey(string value, out ushort keyCode)
    {
        keyCode = 0;
        if (value.Length is < 2 or > 3 || value[0] != 'f' ||
            !int.TryParse(value[1..], out var number) || number is < 1 or > 12)
        {
            return false;
        }

        keyCode = (ushort)(VkF1 + number - 1);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);
}
