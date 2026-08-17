namespace Metis.Core.Services;

/// <summary>
/// What a system command would do, described plainly enough for someone to
/// agree to it or refuse it.
/// </summary>
public sealed record SystemCommandReview(
    string Command,
    bool NeedsElevation,
    bool IsRefused,
    string Summary);

/// <summary>
/// Decides which system commands Metis may offer to run, and how to describe
/// one to the user before it does.
///
/// The premise is that a command is confirmed by a person, not by a policy — so
/// this is not an attempt to sort safe commands from dangerous ones. That
/// distinction does not survive contact with a shell: the same command that
/// reports a device's state takes an extra switch to remove it, and a rule that
/// waves through anything gives the user a habit of approving without reading.
///
/// What this does instead is refuse the small set of things nobody should be
/// agreeing to through an assistant at all, and turn everything else into a
/// sentence a person can actually judge.
/// </summary>
public static class SystemCommandPolicy
{
    /// <summary>
    /// Commands Metis will not offer at any confirmation. These are refused not
    /// because they are the most destructive things possible, but because they
    /// are irreversible or leave the user unable to undo what happened —
    /// reformatting a disk, wiping the boot record, mass file deletion, adding
    /// an account, disabling the defences that would have caught a mistake.
    /// </summary>
    private static readonly (string Pattern, string Why)[] NeverOffered =
    [
        ("format ", "it would format a drive"),
        ("format-volume", "it would format a volume"),
        ("clear-disk", "it would erase a disk"),
        ("remove-partition", "it would remove a partition"),
        ("diskpart", "it drives the partition editor"),
        ("bcdedit", "it changes how Windows boots"),
        ("bootrec", "it rewrites boot records"),
        ("vssadmin delete", "it would delete the shadow copies used to restore files"),
        ("wbadmin delete", "it would delete backups"),
        ("cipher /w", "it would wipe free space irreversibly"),
        ("rd /s", "it would delete a folder tree"),
        ("rmdir /s", "it would delete a folder tree"),
        ("remove-item -recurse", "it would delete a folder tree"),
        ("del /f /s", "it would force-delete a folder tree"),
        ("net user", "it would change Windows accounts"),
        ("new-localuser", "it would create a Windows account"),
        ("add-localgroupmember", "it would change who is an administrator"),
        ("set-executionpolicy", "it would lower PowerShell's own protections"),
        ("set-mppreference", "it would change Microsoft Defender"),
        ("disable-windowsoptionalfeature", "it would remove a Windows feature"),
        ("reg delete", "it would delete registry keys"),
        ("shutdown", "it would shut down or restart the machine mid-task"),
        ("invoke-expression", "it would run text fetched at runtime"),
        ("iex ", "it would run text fetched at runtime"),
        ("downloadstring", "it would run code downloaded from the internet"),
        ("invoke-webrequest", "it would fetch and could run remote content"),
        ("curl ", "it would fetch remote content"),
        ("certutil -urlcache", "it is a common way of downloading and running remote files")
    ];

    /// <summary>
    /// Verbs that need administrator rights. Knowing this in advance lets the
    /// confirmation say so, rather than the user discovering it when Windows
    /// puts up its own prompt behind the window they were looking at.
    /// </summary>
    private static readonly string[] NeedsAdministrator =
    [
        "disable-pnpdevice", "enable-pnpdevice", "pnputil",
        "set-service", "start-service", "stop-service", "restart-service", "sc ",
        "set-netadapter", "disable-netadapter", "enable-netadapter",
        "set-itemproperty hklm", "new-itemproperty hklm", "reg add hklm",
        "dism", "sfc", "bcdboot", "netsh", "takeown", "icacls"
    ];

    public const int MaximumCommandLength = 400;

    /// <summary>
    /// Reviews a command before the user is asked about it.
    /// </summary>
    public static SystemCommandReview Review(string? command)
    {
        var trimmed = (command ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return new SystemCommandReview(string.Empty, false, true, "There was no command to run.");
        }

        if (trimmed.Length > MaximumCommandLength)
        {
            return new SystemCommandReview(trimmed, false, true,
                "That command is too long to show you properly, so Metis will not run it.");
        }

        if (trimmed.Any(char.IsControl))
        {
            return new SystemCommandReview(trimmed, false, true,
                "That command contains hidden characters, so Metis will not run it.");
        }

        var lowered = trimmed.ToLowerInvariant();

        foreach (var (pattern, why) in NeverOffered)
        {
            if (lowered.Contains(pattern, StringComparison.Ordinal))
            {
                return new SystemCommandReview(trimmed, false, true,
                    $"Metis will not run this because {why}. Do it yourself if you meant to.");
            }
        }

        var elevated = NeedsAdministrator.Any(verb => lowered.Contains(verb, StringComparison.Ordinal));

        return new SystemCommandReview(
            trimmed,
            elevated,
            IsRefused: false,
            elevated
                ? "This needs administrator rights, so Windows will ask as well."
                : "This runs as you, without administrator rights.");
    }
}
