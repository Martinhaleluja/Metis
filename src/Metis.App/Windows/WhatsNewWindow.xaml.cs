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
    public const string Version = "3.15.0";

    private sealed record Change(string Icon, string Title, string Detail)
{
    /// <summary>
    /// Without the icon, which is decoration: a screen reader announcing the
    /// emoji before every line adds nothing and takes time.
    /// </summary>
    public override string ToString() => $"{Title}. {Detail}";
}

    /// <summary>
    /// Written for the person using Metis, not for whoever wrote the code.
    /// Each line says what is different now, and where a thing was broken
    /// before, it says so plainly rather than calling it an improvement.
    /// </summary>
    private static readonly IReadOnlyList<Change> Changes =
    [
        new("⚡", "Answers appear as they are written",
            "Metis used to compose its whole reply before showing you any of it, so an ordinary question meant staring at an empty panel for ten to fifteen seconds. The words now arrive as it writes them — usually within a second or two."),

        new("🗨", "Ask the next question straight away",
            "Metis held on to the microphone until it had finished speaking, so for eight to ten seconds after the answer was already on your screen it refused to listen. It is ready as soon as the answer appears, and asking something new interrupts it mid-sentence, the way it should."),

        new("⏱", "It no longer waits for no reason",
            "Three separate places waited for the length of a spoken line twice over, which doubled every pause in a walkthrough. A written answer with the voice turned off was also revealed one word at a time at a fixed speed — a paragraph could take fourteen seconds to finish appearing, long after Metis had it in full. Both are gone."),

        new("🖼", "A lighter look at your screen",
            "The screenshot sent with each question was full size, which cost time to upload and more time for the model to read. Ordinary questions now use a smaller frame, while pointing at a specific control still gets every pixel. Reading the screen and taking the picture also happen at the same time instead of one after the other."),

        new("🧠", "Less deliberating, more answering",
            "Metis never told the model how hard to think, so it thought as long as it liked before writing anything — invisibly, while you waited. It now asks for a quick answer to a quick question, and still takes its time when it is drawing a lesson."),

        new("❌", "Failures fail fast",
            "A rejected request used to be sent again to every other provider in turn, each with its own minute-long timeout, so a problem that could never succeed took over a minute to report. Metis now recognises that case and says so in seconds. Connecting has its own short timeout too, so an unreachable service no longer costs the whole request."),

        new("🤖", "Agents remember their own history",
            "A background agent re-sent everything it had done on every single step, so a thirty-step task read its own history thirty times over. It now carries the conversation forward properly and reuses what it has already read, which makes long tasks noticeably quicker and much cheaper. They also default to a current model instead of one from early last year."),

        new("📊", "It records how long it took",
            "Every turn now writes a line to the log saying where the time went — capturing, reading the screen, waiting for the first word, and the total. If Metis feels slow for you, that log now says why.")
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
