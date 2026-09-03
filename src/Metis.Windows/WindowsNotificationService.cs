using System.Runtime.InteropServices;
using System.Security;

// Fully qualified because this namespace is itself called Metis.Windows, so a
// bare "Windows.UI.Notifications" binds to the wrong thing.
using XmlDocument = global::Windows.Data.Xml.Dom.XmlDocument;
using ToastNotification = global::Windows.UI.Notifications.ToastNotification;
using ToastNotificationManager = global::Windows.UI.Notifications.ToastNotificationManager;
using ToastNotifier = global::Windows.UI.Notifications.ToastNotifier;
using ToastActivatedEventArgs = global::Windows.UI.Notifications.ToastActivatedEventArgs;
using NotificationSetting = global::Windows.UI.Notifications.NotificationSetting;

namespace Metis.Windows;

public interface IWindowsNotificationService
{
    void ShowNotification(string title, string message, string? attribution = null);

    /// <summary>
    /// Shows a notification the user can act on without opening Metis first.
    /// </summary>
    /// <param name="arguments">
    /// Carried back when the toast body is clicked, so the right task can be
    /// opened rather than just the app.
    /// </param>
    void ShowActionableNotification(
        string title,
        string message,
        string arguments,
        IReadOnlyList<(string Label, string Arguments)> buttons);

    /// <summary>
    /// Whether the last attempt reached Windows, and what went wrong if not.
    ///
    /// Toast failure is completely silent from the user's side, so something
    /// has to be able to say "these are not working". Nothing could before.
    /// </summary>
    string? LastFailure { get; }

    /// <summary>Raised when the user clicks a toast or one of its buttons.</summary>
    event EventHandler<string>? Activated;
}

/// <summary>
/// Windows notifications for an unpackaged desktop app.
///
/// Two things had to be true for these to appear and only one of them was.
/// The process sets an AppUserModelID, which it did. And a Start Menu shortcut
/// has to carry the same id, which nothing ever wrote — so Windows had no
/// record of the app the toast claimed to be from and dropped it. See
/// <see cref="ToastRegistration"/>.
///
/// The failure was invisible twice over: Windows does not report a dropped
/// toast, and the old code caught every exception into <c>Debug.WriteLine</c>,
/// which does nothing in a release build. Both are fixed here — the identity is
/// registered at startup, and a failure is kept where the app can show it.
/// </summary>
public sealed class WindowsNotificationService : IWindowsNotificationService
{
    private readonly Action<string>? _log;
    private ToastNotifier? _notifier;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    public string? LastFailure { get; private set; }

    public event EventHandler<string>? Activated;

    public WindowsNotificationService(Action<string>? log = null)
    {
        _log = log;

        try
        {
            SetCurrentProcessExplicitAppUserModelID(ToastRegistration.AppUserModelId);
        }
        catch (Exception exception)
        {
            LastFailure = $"Could not set the app identity: {exception.Message}";
            _log?.Invoke(LastFailure);
        }
    }

    /// <summary>
    /// Registers the identity with Windows and confirms notifications can be
    /// shown. Called once at startup.
    /// </summary>
    public void Register(string executablePath)
    {
        // Try first, repair only if needed. Windows is the authority on whether
        // it will accept a toast, so asking it beats inspecting the shortcut
        // ourselves — and it means a healthy install is left completely alone
        // rather than having its Start Menu entry rewritten on every launch.
        if (TryCreateNotifier(quiet: true))
        {
            _log?.Invoke("Windows notifications are enabled.");
            return;
        }

        var outcome = ToastRegistration.EnsureShortcut(executablePath);
        _log?.Invoke(outcome);

        try
        {
            _notifier = ToastNotificationManager.CreateToastNotifier(ToastRegistration.AppUserModelId);
            var setting = _notifier.Setting;

            if (setting != NotificationSetting.Enabled)
            {
                LastFailure = $"Windows is not accepting Metis notifications: {setting}.";
                _log?.Invoke(LastFailure);
            }
            else
            {
                LastFailure = null;
                _log?.Invoke("Windows notifications are enabled.");
            }
        }
        catch (Exception exception)
        {
            // The HRESULT is the useful part. A COM failure here often arrives
            // with an empty Message, which says nothing at all.
            LastFailure =
                $"Notifications are unavailable: {exception.GetType().Name} 0x{exception.HResult:X8} "
                + $"{(string.IsNullOrWhiteSpace(exception.Message) ? "(no message)" : exception.Message)}";
            _log?.Invoke(LastFailure);
        }
    }

    /// <summary>
    /// Attempts to get a notifier, reporting nothing on failure.
    ///
    /// A newly written Start Menu shortcut is not visible to the notification
    /// platform immediately, so the first run after an install can fail here
    /// and succeed on the next launch. That is why showing a notification
    /// retries this rather than relying on what startup concluded.
    /// </summary>
    private bool TryCreateNotifier(bool quiet)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(ToastRegistration.AppUserModelId);
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                if (!quiet)
                {
                    LastFailure = $"Windows is not accepting Metis notifications: {notifier.Setting}.";
                }

                return false;
            }

            _notifier = notifier;
            LastFailure = null;
            return true;
        }
        catch (Exception exception)
        {
            if (!quiet)
            {
                LastFailure =
                    $"Notifications are unavailable: {exception.GetType().Name} 0x{exception.HResult:X8} "
                    + $"{(string.IsNullOrWhiteSpace(exception.Message) ? "(no message)" : exception.Message)}";
            }

            return false;
        }
    }

    public void ShowNotification(string title, string message, string? attribution = null) =>
        ShowActionableNotification(title, message, arguments: string.Empty, buttons: []);

    public void ShowActionableNotification(
        string title,
        string message,
        string arguments,
        IReadOnlyList<(string Label, string Arguments)> buttons)
    {
        Task.Run(() =>
        {
            try
            {
                var document = new XmlDocument();
                document.LoadXml(BuildToastXml(title, message, arguments, buttons));

                var toast = new ToastNotification(document);
                toast.Activated += (_, e) =>
                {
                    var chosen = (e as ToastActivatedEventArgs)?.Arguments ?? arguments;
                    Activated?.Invoke(this, chosen);
                };

                toast.Failed += (_, e) =>
                {
                    LastFailure = $"Windows rejected the notification: {e.ErrorCode.Message}";
                    _log?.Invoke(LastFailure);
                };

                var notifier = _notifier ??= ToastNotificationManager.CreateToastNotifier(ToastRegistration.AppUserModelId);
                notifier.Show(toast);
            }
            catch (Exception exception)
            {
                // Kept rather than dropped. A silent notification system that
                // has quietly stopped working is worse than one that says so.
                LastFailure = $"Notification failed: {exception.Message}";
                _log?.Invoke(LastFailure);
            }
        });
    }

    /// <summary>
    /// Builds the toast by hand rather than from a template.
    ///
    /// The old code used ToastText02, which is two lines of text and nothing
    /// else — so an agent's id, its goal and its result were concatenated into
    /// one wrapping line that Windows then truncated. ToastGeneric gives a
    /// proper title, body, attribution and, most usefully, buttons: an approval
    /// can be answered from the notification instead of by finding the app.
    /// </summary>
    private static string BuildToastXml(
        string title,
        string message,
        string arguments,
        IReadOnlyList<(string Label, string Arguments)> buttons)
    {
        var actions = string.Empty;

        if (buttons.Count > 0)
        {
            var items = buttons.Select(b =>
                $"<action content='{Escape(b.Label)}' arguments='{Escape(b.Arguments)}' activationType='foreground'/>");
            actions = $"<actions>{string.Join(string.Empty, items)}</actions>";
        }

        return $"""
            <toast launch='{Escape(arguments)}' activationType='foreground'>
              <visual>
                <binding template='ToastGeneric'>
                  <text>{Escape(title)}</text>
                  <text>{Escape(message)}</text>
                </binding>
              </visual>
              {actions}
              <audio src='ms-winsoundevent:Notification.Default'/>
            </toast>
            """;
    }

    private static string Escape(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
}
