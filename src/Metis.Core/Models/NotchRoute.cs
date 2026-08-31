namespace Metis.Core.Models;

/// <summary>
/// Which surface the notch is showing.
///
/// <see cref="Rest"/> is the resting sliver, and it is a page like any other so
/// that "nothing is open" is a value rather than four booleans that all happen
/// to be false.
/// </summary>
public enum NotchPage
{
    Rest,

    /// <summary>The conversation.</summary>
    Chat,

    /// <summary>Signing in or creating an account.</summary>
    SignIn,

    /// <summary>What Metis needs permission to do, shown once on first run.</summary>
    Welcome,

    /// <summary>Settings — a menu, or one section of it.</summary>
    Settings,

    /// <summary>The list of background agents.</summary>
    Agents,

    /// <summary>Starting a background agent.</summary>
    SpawnAgent,

    /// <summary>What changed in this release.</summary>
    WhatsNew
}

/// <summary>
/// A place in the notch: a page, and for settings, which section of it.
/// </summary>
/// <param name="Section">
/// Only meaningful for <see cref="NotchPage.Settings"/>. Null means the settings
/// menu itself, which is why settings is two levels rather than eleven flat
/// pages — one route with a section beneath it, so a person can get back to the
/// list they came from.
/// </param>
/// <param name="Modal">
/// Whether this page may be interrupted. First run sets it: a background agent
/// finishing must not close the sign-in form out from under someone typing a
/// password into it.
/// </param>
public sealed record NotchRoute(NotchPage Page, string? Section = null, bool Modal = false)
{
    public static NotchRoute Rest { get; } = new(NotchPage.Rest);

    public static NotchRoute Chat { get; } = new(NotchPage.Chat);

    public static NotchRoute Agents { get; } = new(NotchPage.Agents);

    public static NotchRoute SpawnAgent { get; } = new(NotchPage.SpawnAgent);

    public static NotchRoute Settings { get; } = new(NotchPage.Settings);

    public static NotchRoute SettingsSection(string section) => new(NotchPage.Settings, section);

    public bool IsRest => Page == NotchPage.Rest;
}

/// <summary>
/// What a navigation attempt should actually do.
///
/// The shell needs to tell three cases apart, and collapsing them is how the
/// current code ended up with four fifty-line Open methods that each re-derive
/// the answer slightly differently: a request that opens something, a request
/// that is already satisfied and should only take focus, and a request that must
/// be refused.
/// </summary>
public enum NotchTransition
{
    /// <summary>Show it. The back stack grows.</summary>
    Open,

    /// <summary>Show it, but do not add a way back to where we were.</summary>
    Replace,

    /// <summary>Already there. Take the keyboard, change nothing else.</summary>
    AlreadyThere,

    /// <summary>Go back to the resting sliver.</summary>
    Close,

    /// <summary>Not now: something is tracing, or a modal page has the floor.</summary>
    Refused
}

/// <summary>
/// Where the notch may go from where it is.
///
/// This is a pure decision with no window attached, kept here for the same
/// reason <c>StartupAuthGate</c> and <c>ProviderRouting</c> are: it is the kind
/// of thing that locks a user out or steals their keyboard when it is wrong, and
/// the shell it belongs to is seventeen hundred lines of WPF that no test can
/// reach. Every rule below used to be a hand-written guard at the top of one of
/// four near-identical methods, which is precisely how they drifted apart.
/// </summary>
public sealed class NotchNavigator
{
    /// <summary>
    /// How far back the trail is remembered.
    ///
    /// Deep enough for settings-menu → section → chat and out again, shallow
    /// enough that a user who has been clicking around for ten minutes is never
    /// more than a few presses from the resting state. An unbounded stack would
    /// mean Escape sometimes takes twenty presses to leave.
    /// </summary>
    public const int MaxDepth = 8;

    private readonly List<NotchRoute> _back = [];

    public NotchRoute Current { get; private set; } = NotchRoute.Rest;

    public bool CanGoBack => _back.Count > 0;

    public int Depth => _back.Count;

    /// <summary>Whether anything other than the resting sliver is showing.</summary>
    public bool IsOpen => !Current.IsRest;

    /// <summary>
    /// What should happen if the notch is asked to show <paramref name="route"/>.
    ///
    /// <paramref name="isTracing"/> refuses everything while the user is drawing
    /// on their own screen — a panel appearing mid-gesture would cover the thing
    /// they are tracing around.
    /// </summary>
    public NotchTransition Evaluate(NotchRoute route, bool isTracing, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (route.IsRest)
        {
            return NotchTransition.Close;
        }

        if (!force && isTracing)
        {
            return NotchTransition.Refused;
        }

        // A modal page holds the floor. First run is the case that matters: an
        // agent finishing, or an update arriving, must not take the screen away
        // from somebody halfway through typing a password.
        if (!force && Current.Modal && Current != route)
        {
            return NotchTransition.Refused;
        }

        if (Current == route)
        {
            return NotchTransition.AlreadyThere;
        }

        // Stepping between sections of settings replaces rather than stacks, so
        // Back from a section returns to the menu instead of walking every
        // section the user happened to look at on the way.
        if (Current.Page == NotchPage.Settings
            && route.Page == NotchPage.Settings
            && Current.Section is not null
            && route.Section is not null)
        {
            return NotchTransition.Replace;
        }

        return NotchTransition.Open;
    }

    /// <summary>
    /// Commits a navigation and returns what was done, so the caller animates
    /// once rather than deciding twice.
    /// </summary>
    public NotchTransition Navigate(NotchRoute route, bool isTracing, bool force = false)
    {
        var transition = Evaluate(route, isTracing, force);

        switch (transition)
        {
            case NotchTransition.Open:
                if (!Current.IsRest)
                {
                    _back.Add(Current);
                    if (_back.Count > MaxDepth)
                    {
                        _back.RemoveAt(0);
                    }
                }

                Current = route;
                break;

            case NotchTransition.Replace:
                Current = route;
                break;

            case NotchTransition.Close:
                Current = NotchRoute.Rest;
                _back.Clear();
                break;
        }

        return transition;
    }

    /// <summary>
    /// The previous page, or null when there is nowhere left to go but rest.
    /// </summary>
    public NotchRoute? GoBack()
    {
        if (_back.Count == 0)
        {
            return null;
        }

        var previous = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Current = previous;
        return previous;
    }

    /// <summary>Back to the resting sliver, forgetting the trail.</summary>
    public void Close()
    {
        Current = NotchRoute.Rest;
        _back.Clear();
    }
}
