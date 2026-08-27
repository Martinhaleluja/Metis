using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Metis.Windows;

/// <summary>
/// Makes Windows willing to show Metis a toast at all.
///
/// This is the missing piece that made every agent notification silently do
/// nothing. An unpackaged desktop application has no package identity, so
/// Windows identifies it by an AppUserModelID — and it will only accept toasts
/// for an AUMID that it can find on a Start Menu shortcut. Metis set the AUMID
/// on the process, correctly, but nothing ever wrote it onto a shortcut. The
/// installer creates one with no such property.
///
/// So `CreateToastNotifier().Show(toast)` was being called, throwing or being
/// dropped, and the exception was swallowed by a `catch` that only wrote to the
/// debugger. Every "agent finished" notification since the feature was built
/// has gone nowhere, and nothing said so.
///
/// The repair is done from the application rather than only from the installer,
/// because installs already exist in the field with a shortcut that lacks the
/// property. Fixing it in the installer alone would leave those broken forever.
/// </summary>
public static class ToastRegistration
{
    /// <summary>
    /// The identity Windows knows Metis by. Must match what the process sets
    /// and what is written onto the shortcut — if the two ever disagree, toasts
    /// stop appearing again with no error.
    ///
    /// This is the one place the original author's name survives, and it stays
    /// deliberately. It is not shown anywhere: Windows lists the app by the
    /// shortcut's name, not by this string. Changing it was tried and measured,
    /// and it silently disabled notifications — the toast platform answered
    /// 0x80070490 (ERROR_NOT_FOUND) for the new identity on every launch and
    /// never picked it up, because Windows only accepts an AppUserModelID it
    /// has already indexed from the Start Menu. Renaming it therefore needs a
    /// migration rather than an edit, and would cost every existing install its
    /// notifications until the shell caught up.
    /// </summary>
    public const string AppUserModelId = "Martin Nakasole.Metis";

    /// <summary>
    /// Ensures a Start Menu shortcut exists carrying the AUMID.
    /// </summary>
    /// <returns>
    /// What happened, for the log. Toast delivery is invisible when it fails,
    /// so the one chance to notice a problem is here.
    /// </returns>
    public static string EnsureShortcut(string executablePath, string displayName = "Metis")
    {
        try
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var programs = Path.Combine(startMenu, "Programs");
            Directory.CreateDirectory(programs);

            var shortcutPath = Path.Combine(programs, $"{displayName}.lnk");

            if (File.Exists(shortcutPath) && HasMatchingAumid(shortcutPath))
            {
                return $"Notification shortcut already correct at {shortcutPath}.";
            }

            WriteShortcut(shortcutPath, executablePath);
            return $"Notification shortcut written to {shortcutPath}.";
        }
        catch (Exception exception)
        {
            return $"Could not register for notifications: {exception.Message}";
        }
    }

    private static bool HasMatchingAumid(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);

            var store = (IPropertyStore)link;
            var key = PropertyKeys.AppUserModelId;
            store.GetValue(ref key, out var value);

            try
            {
                return value.VariantType == VarEnum.VT_LPWSTR &&
                       string.Equals(
                           Marshal.PtrToStringUni(value.Pointer),
                           AppUserModelId,
                           StringComparison.Ordinal);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            // Unreadable means it needs rewriting, which is what the caller
            // does next.
            return false;
        }
    }

    private static void WriteShortcut(string shortcutPath, string executablePath)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(executablePath);
        link.SetArguments(string.Empty);
        link.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? string.Empty);
        link.SetDescription("Learn the digital world by doing");

        var store = (IPropertyStore)link;
        var key = PropertyKeys.AppUserModelId;

        var value = new PropVariant
        {
            VariantType = VarEnum.VT_LPWSTR,
            Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId)
        };

        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    // ------------------------------------------------------------- interop

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    private static class PropertyKeys
    {
        /// <summary>System.AppUserModel.ID</summary>
        public static PropertyKey AppUserModelId => new(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public VarEnum VariantType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxArgs);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
