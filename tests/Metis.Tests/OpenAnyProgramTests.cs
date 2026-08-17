using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// Metis has to open any installed program, not a list of known ones.
///
/// Two routes do that: a direct shell launch, which resolves anything on PATH
/// or carrying an App Paths entry, and Windows Search, which reaches Store apps
/// known only by a display name. These cases cover what may take the first
/// route — because that one executes what it is handed — and how a display name
/// is turned into the executable it belongs to.
/// </summary>
public sealed class OpenAnyProgramTests
{
    [Theory]
    [InlineData("notepad")]
    [InlineData("mspaint")]
    [InlineData("Google Chrome")]
    [InlineData("Visual Studio Code")]
    [InlineData("chrome.exe")]
    [InlineData("7-Zip File Manager")]
    public void An_ordinary_program_name_may_be_launched_directly(string name) =>
        Assert.True(NativePhysicalDesktopInput.IsBareProgramName(name));

    /// <summary>
    /// The direct route hands its argument to the shell, so anything that could
    /// turn a name into a path, a switch, or a second command is refused. Those
    /// fall through to the search route, where they are typed as literal text
    /// into a search box instead of being executed.
    /// </summary>
    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("cmd & del everything")]
    [InlineData("powershell | iex")]
    [InlineData("..\\..\\something")]
    [InlineData("-NoProfile")]
    [InlineData("app > file")]
    [InlineData("say \"hello\"")]
    [InlineData("%SystemRoot%")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_that_is_not_a_bare_name_is_refused(string name) =>
        Assert.False(NativePhysicalDesktopInput.IsBareProgramName(name));

    [Fact]
    public void An_absurdly_long_name_is_refused() =>
        Assert.False(NativePhysicalDesktopInput.IsBareProgramName(new string('a', 200)));

    /// <summary>
    /// Installers register their executables under short names, so a display
    /// name has to be tried as itself, with an extension, and as its last word:
    /// "Google Chrome" is registered as chrome.exe.
    /// </summary>
    [Fact]
    public void A_display_name_is_tried_as_its_executable_too()
    {
        var candidates = NativePhysicalDesktopInput.Candidates("Google Chrome").ToArray();

        Assert.Equal(["Google Chrome", "Google Chrome.exe", "Chrome.exe"], candidates);
    }

    [Fact]
    public void A_single_word_is_not_tried_twice()
    {
        var candidates = NativePhysicalDesktopInput.Candidates("notepad").ToArray();

        Assert.Equal(["notepad", "notepad.exe"], candidates);
    }

    [Fact]
    public void A_name_that_already_carries_its_extension_is_left_alone()
    {
        var candidates = NativePhysicalDesktopInput.Candidates("chrome.exe").ToArray();

        Assert.Equal(["chrome.exe"], candidates);
    }
}
