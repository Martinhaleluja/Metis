using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Metis.Core.Services;

using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;

namespace Metis.App.Windows;

/// <summary>
/// What changed since the version the user last ran.
///
/// Shown once after an update and never again, because a release note is only
/// interesting the first time. It exists because Metis updates itself in the
/// background for testers: without this, a build simply appears with different
/// behaviour and no explanation, and the things most worth knowing about this
/// release — that agents can now write files and drive a browser — are exactly
/// the ones nobody should discover by accident.
/// </summary>
public partial class WhatsNewWindow : Window
{
    /// <summary>
    /// The release this window describes.
    ///
    /// Kept beside the notes rather than read from the assembly, so that
    /// editing one without the other is obvious. The check against the running
    /// build uses <see cref="AppVersion.Current"/>.
    /// </summary>
    public const string Version = "3.14.0";

    private sealed record Change(string Icon, string Title, string Detail);

    /// <summary>
    /// Written for the person using Metis, not for whoever wrote the code.
    /// Each line says what is different now, and where a thing was broken
    /// before, it says so plainly rather than calling it an improvement.
    /// </summary>
    private static readonly IReadOnlyList<Change> Changes =
    [
        new("🌓", "Light mode reaches the notch",
            "The notch and the chat inside it stayed black when the rest of Metis went light, and any text on them that took its colour from the theme turned black too and disappeared. Both now follow whichever theme you have chosen, and switching theme changes them while they are open."),

        new("\U0001F916", "Just ask for an agent",
            "Telling Metis to start an agent now actually starts one, however you phrase it — \"have an agent tidy my downloads\", or \"spawn one\" and answering when it asks what for. Ask for two and you get two. Before this it usually described the agent instead of running it."),

        new("\U0001F6E0", "Agents can build things",
            "They can search inside files, edit part of a file instead of rewriting the whole thing, and start a dev server and check whether it actually works. That last one was impossible before — nothing an agent started could outlive the command that started it."),

        new("\U0001F4C1", "Each agent works in its own folder",
            "An agent is confined to its own workspace unless you point it at one of your folders when you start it. Previously they could read and write anywhere in your user profile."),

        new("\U0001F310", "A browser you can watch",
            "Agents can drive a real Chrome window — visible, with a banner across the top saying an agent is working there. You can switch away and carry on with your own work while it runs."),

        new("\U0001F510", "It hands the browser back for anything sensitive",
            "At a login, a sign-up, a payment page, or a \"are you human\" check, the agent stops and gives the browser to you. It will not type a password or card number, and it does not try to get around those checks."),

        new("\U0001F514", "Notifications actually appear",
            "Agent notifications had never worked: Windows was dropping every one, and the failure was invisible. They arrive now, and carry Approve and Deny buttons so you can answer an agent without finding the window."),

        new("\U0001F4CB", "The agent panel shows what happened",
            "Files an agent produced are listed and open when clicked — there was no way to reach them before. An approval now names the tool and its arguments instead of asking you to allow something unspecified. Plus how long it took, where it worked, and a way to clear finished tasks."),

        new("\U0001F5E3", "Voice works again",
            "Speech had been pointed at a model that cannot produce audio and no longer exists, so it failed silently every time. Fixed, and your saved setting is corrected automatically."),

        new("\U0001F393", "Walkthroughs notice whether you kept up",
            "A lesson now checks the screen between steps and points something out again if it hasn't happened, rather than marching ahead. It never blocks — anything it cannot read from the screen carries on as before."),

        new("\U0001F50D", "It looks twice instead of guessing",
            "When Metis cannot confirm what you are asking about, it now goes and finds the control through Windows itself, and says it cannot see the thing rather than marking a confident wrong spot.")
    ];

    public WhatsNewWindow()
    {
        InitializeComponent();

        VersionBadge.Text = $"v{Version}";
        FooterText.Text = $"Metis v{Version}";

        BuildChanges();
        Loaded += (_, _) => StaggerIn();
    }

    /// <summary>
    /// Whether this build's notes still need showing.
    /// </summary>
    /// <param name="lastSeenVersion">What the user has already been shown.</param>
    /// <remarks>
    /// A first install shows nothing. Someone meeting Metis for the first time
    /// is being introduced to all of it at once, and a list of what changed
    /// since a version they never ran is noise at exactly the wrong moment.
    /// </remarks>
    public static bool ShouldShow(string? lastSeenVersion)
    {
        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            return false;
        }

        return !string.Equals(lastSeenVersion.Trim(), Version, StringComparison.OrdinalIgnoreCase);
    }

    private void BuildChanges()
    {
        foreach (var change in Changes)
        {
            // A Grid rather than a horizontal StackPanel. A StackPanel measures
            // its children with infinite width in the direction it stacks, so
            // TextWrapping never engages and every line runs off the edge --
            // which is exactly what the first version of this window did.
            var row = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(0, 0, 0, 16),
                Opacity = 0,
                RenderTransform = new TranslateTransform(0, 10)
            };

            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = System.Windows.GridLength.Auto
            });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
            });

            var icon = new TextBlock
            {
                Text = change.Icon,
                FontSize = 17,
                Margin = new Thickness(0, 1, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            System.Windows.Controls.Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = change.Title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("TextBrush")
            });
            text.Children.Add(new TextBlock
            {
                Text = change.Detail,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("MutedBrush")
            });

            System.Windows.Controls.Grid.SetColumn(text, 1);
            row.Children.Add(text);
            ChangeList.Children.Add(row);
        }
    }

    /// <summary>
    /// Brings the entries in one after another, the same 40 ms step the trace
    /// toolbar uses, so the list reads downward instead of arriving as a wall.
    /// </summary>
    private void StaggerIn()
    {
        var index = 0;

        foreach (var child in ChangeList.Children)
        {
            if (child is not System.Windows.Controls.Grid row)
            {
                continue;
            }

            var start = TimeSpan.FromMilliseconds(index * 40);
            var duration = TimeSpan.FromMilliseconds(240);

            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = start });

            if (row.RenderTransform is TranslateTransform rise)
            {
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration)
                {
                    BeginTime = start,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }

            index++;
        }
    }

    private Brush Resource(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
