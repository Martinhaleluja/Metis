using System.Text.RegularExpressions;

namespace Metis.Tests;

/// <summary>
/// Every view-model record bound to a list has to say what it is.
///
/// This is a source scan rather than a behavioural test because the thing it
/// guards cannot be reached from here: the records live in the desktop project,
/// which this project does not reference, and the failure only appears in a
/// screen reader.
///
/// The bug it exists for is one WPF makes very easy to write and impossible to
/// see. An ItemsControl hands its data item to UI Automation as the name of
/// each generated container, so a record with no ToString is announced as its
/// compiler-generated dump: the type name, then every field, brushes and
/// visibilities included. It was found by reading the automation tree of a
/// running Metis and finding a settings menu that read out
/// "SectionRow { Key = Account, Title = Account and plan, Summary = ... }",
/// a conversation that read out "Metis.App.Windows.ChatBubble" three times,
/// a colour picker announcing "Swatch { Name = Sky, Fill = #FF8ED8FF }" ten
/// times over, and an agent drawer reading several hundred characters of
/// hexadecimal per task. Four separate places, none of which looked wrong on
/// screen, none of which failed a test, and all of which were written the same
/// way because that is the obvious way to write them.
///
/// So the rule is checked mechanically: a record declared in a window's
/// code-behind file that binds anything to a list must override ToString.
/// </summary>
public sealed class ViewModelAnnouncementTests
{
    private static string WindowsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Metis.App", "Windows");
    }

    /// <summary>
    /// Matches a positional record declaration and captures its name, whether
    /// it is followed by a body, and where that body starts.
    /// </summary>
    private static readonly Regex RecordDeclaration = new(
        @"record\s+(?<name>[A-Z][A-Za-z0-9]*)\s*\(",
        RegexOptions.Compiled);

    public static TheoryData<string> WindowFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(WindowsDirectory(), "*.xaml.cs"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WindowFiles))]
    public void Records_in_a_list_bound_window_announce_themselves(string fileName)
    {
        var path = Path.Combine(WindowsDirectory(), fileName);
        var source = File.ReadAllText(path);

        // Only files that actually put something in a list. A record used for
        // anything else is never handed to UI Automation and needs nothing.
        if (!source.Contains("ItemsSource", StringComparison.Ordinal))
        {
            return;
        }

        foreach (Match match in RecordDeclaration.Matches(source))
        {
            var name = match.Groups["name"].Value;

            // The declaration's body, if it has one: from the close of the
            // parameter list to the next blank line at the same nesting, which
            // is close enough given every one of these is a short record.
            var body = BodyOf(source, match.Index);

            Assert.True(
                body.Contains("override string ToString", StringComparison.Ordinal),
                $"{fileName}: the record '{name}' is declared in a file that binds a list, "
                + "but does not override ToString. WPF will announce its compiler-generated "
                + "field dump to screen readers. Give it a sentence a person would want read "
                + "aloud, or move it to a file that binds nothing.");
        }
    }

    /// <summary>
    /// Everything from a record declaration to the end of its body, taking the
    /// terminating semicolon as an empty body.
    /// </summary>
    private static string BodyOf(string source, int start)
    {
        var index = source.IndexOf('(', start);
        var depth = 0;

        for (; index < source.Length; index++)
        {
            if (source[index] == '(')
            {
                depth++;
            }
            else if (source[index] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }
        }

        // What follows the parameter list is either ";" — no body — or "{ ... }".
        var rest = source[Math.Min(index + 1, source.Length)..];
        var brace = rest.IndexOf('{');
        var semicolon = rest.IndexOf(';');

        if (brace < 0 || (semicolon >= 0 && semicolon < brace))
        {
            return string.Empty;
        }

        depth = 0;
        for (var i = brace; i < rest.Length; i++)
        {
            if (rest[i] == '{')
            {
                depth++;
            }
            else if (rest[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return rest[brace..(i + 1)];
                }
            }
        }

        return rest[brace..];
    }

    /// <summary>
    /// Proves the scan can fail. A source-scanning test that only ever passes
    /// is indistinguishable from one that is not looking at anything, and this
    /// project has shipped exactly that mistake before: a regression test whose
    /// pattern could not match the bug it was written for stayed green through
    /// the bug being reintroduced.
    /// </summary>
    [Fact]
    public void The_scan_would_notice_a_record_without_one()
    {
        const string offending = """
            public sealed record Leaky(string Name, string Detail);
            """;

        Assert.Empty(BodyOf(offending, 0));
    }

    [Fact]
    public void The_scan_recognises_a_record_that_has_one()
    {
        const string good = """
            public sealed record Fine(string Name)
            {
                public override string ToString() => Name;
            }
            """;

        Assert.Contains("override string ToString", BodyOf(good, 0));
    }
}
