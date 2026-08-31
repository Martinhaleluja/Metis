using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// Where the notch may go, and how a person gets back out.
///
/// These rules used to be hand-written guards at the top of four near-identical
/// fifty-line methods, which is exactly how they drifted: three of the four
/// refused to open while the user was tracing, one did not; three closed the
/// others first, in three slightly different orders. With one navigator the
/// rules are written once, and here is where they are held to.
/// </summary>
public sealed class NotchNavigatorTests
{
    [Fact]
    public void It_starts_at_rest()
    {
        var navigator = new NotchNavigator();

        Assert.True(navigator.Current.IsRest);
        Assert.False(navigator.IsOpen);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Opening_a_page_makes_it_current()
    {
        var navigator = new NotchNavigator();

        Assert.Equal(NotchTransition.Open, navigator.Navigate(NotchRoute.Chat, isTracing: false));
        Assert.Equal(NotchRoute.Chat, navigator.Current);
        Assert.True(navigator.IsOpen);
    }

    /// <summary>
    /// Opening from rest leaves nothing to go back to — rest is where Back would
    /// have taken you anyway, and offering a back arrow that does the same thing
    /// as the close button is two controls for one action.
    /// </summary>
    [Fact]
    public void Opening_from_rest_leaves_nothing_behind()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);

        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Opening_from_a_page_remembers_where_you_were()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);
        navigator.Navigate(NotchRoute.Agents, isTracing: false);

        Assert.True(navigator.CanGoBack);
        Assert.Equal(NotchRoute.Chat, navigator.GoBack());
        Assert.Equal(NotchRoute.Chat, navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Going_back_with_nowhere_to_go_says_so()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);

        Assert.Null(navigator.GoBack());
    }

    /// <summary>
    /// Asking for the page already showing takes the keyboard and changes
    /// nothing else. Re-running the open animation because somebody pressed the
    /// same shortcut twice is a flicker with no meaning.
    /// </summary>
    [Fact]
    public void Asking_for_the_page_already_open_is_not_a_transition()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);

        Assert.Equal(NotchTransition.AlreadyThere, navigator.Navigate(NotchRoute.Chat, isTracing: false));
        Assert.False(navigator.CanGoBack);
    }

    /// <summary>
    /// Nothing opens over a trace. The user is drawing on their own screen, and
    /// a panel appearing mid-gesture covers the thing they are drawing around.
    /// </summary>
    [Fact]
    public void Nothing_opens_while_the_user_is_tracing()
    {
        var navigator = new NotchNavigator();

        Assert.Equal(NotchTransition.Refused, navigator.Navigate(NotchRoute.Chat, isTracing: true));
        Assert.True(navigator.Current.IsRest);
    }

    /// <summary>
    /// The rule that protects first run. A background agent finishing, or an
    /// update arriving, must not take the screen away from somebody halfway
    /// through typing a password.
    /// </summary>
    [Fact]
    public void A_modal_page_cannot_be_interrupted()
    {
        var navigator = new NotchNavigator();
        var signIn = new NotchRoute(NotchPage.SignIn, Modal: true);
        navigator.Navigate(signIn, isTracing: false);

        Assert.Equal(NotchTransition.Refused, navigator.Navigate(NotchRoute.Agents, isTracing: false));
        Assert.Equal(signIn, navigator.Current);
    }

    /// <summary>
    /// First run itself still has to be able to move on, so the flow that owns
    /// the modal can force past it.
    /// </summary>
    [Fact]
    public void A_modal_page_can_be_left_deliberately()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(new NotchRoute(NotchPage.SignIn, Modal: true), isTracing: false);

        var welcome = new NotchRoute(NotchPage.Welcome, Modal: true);
        Assert.Equal(NotchTransition.Open, navigator.Navigate(welcome, isTracing: false, force: true));
        Assert.Equal(welcome, navigator.Current);
    }

    // ------------------------------- Settings -------------------------------

    /// <summary>
    /// Settings is two levels: a menu, and a section inside it. Back from a
    /// section returns to the menu.
    /// </summary>
    [Fact]
    public void A_settings_section_can_be_backed_out_of_to_the_menu()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Settings, isTracing: false);
        navigator.Navigate(NotchRoute.SettingsSection("Intelligence"), isTracing: false);

        Assert.Equal(NotchRoute.Settings, navigator.GoBack());
    }

    /// <summary>
    /// Moving between two sections replaces rather than stacks. Otherwise Back
    /// walks every section the user happened to glance at on the way, which is
    /// not where they think they came from.
    /// </summary>
    [Fact]
    public void Moving_between_sections_does_not_pile_up_history()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Settings, isTracing: false);
        navigator.Navigate(NotchRoute.SettingsSection("Intelligence"), isTracing: false);

        Assert.Equal(
            NotchTransition.Replace,
            navigator.Navigate(NotchRoute.SettingsSection("Voice"), isTracing: false));

        Assert.Equal(NotchRoute.SettingsSection("Voice"), navigator.Current);
        Assert.Equal(NotchRoute.Settings, navigator.GoBack());
    }

    // -------------------------------- Closing --------------------------------

    [Fact]
    public void Closing_forgets_the_trail()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);
        navigator.Navigate(NotchRoute.Agents, isTracing: false);
        navigator.Close();

        Assert.True(navigator.Current.IsRest);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Navigating_to_rest_is_closing() =>
        Assert.Equal(
            NotchTransition.Close,
            new NotchNavigator().Navigate(NotchRoute.Rest, isTracing: false));

    /// <summary>
    /// The trail is bounded. Somebody who has been clicking around for ten
    /// minutes should never be twenty presses from the resting state.
    /// </summary>
    [Fact]
    public void The_trail_has_an_end()
    {
        var navigator = new NotchNavigator();
        navigator.Navigate(NotchRoute.Chat, isTracing: false);

        for (var i = 0; i < NotchNavigator.MaxDepth + 5; i++)
        {
            navigator.Navigate(new NotchRoute(NotchPage.Settings, $"Section{i}"), isTracing: false);
        }

        Assert.True(navigator.Depth <= NotchNavigator.MaxDepth);
    }
}
