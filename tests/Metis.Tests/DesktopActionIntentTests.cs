using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// In Guide mode this decision is the difference between Metis performing a
/// step and merely pointing at it. The commands below are the ones that were
/// silently withheld in real use because the matcher only knew "open the".
/// </summary>
public sealed class DesktopActionIntentTests
{
    [Theory]
    [InlineData("open chrome")]
    [InlineData("Open Chrome and go to youtube")]
    [InlineData("launch notepad")]
    [InlineData("click the export button")]
    [InlineData("close this tab")]
    [InlineData("switch to the browser")]
    [InlineData("scroll down")]
    [InlineData("type my email address")]
    [InlineData("search for wireless headphones")]
    [InlineData("minimise everything")]
    [InlineData("do it for me")]
    public void A_plain_command_counts_as_asking_metis_to_act(string request) =>
        Assert.True(RequestIntent.IsComputerActionRequest(request), $"'{request}' should read as a command");

    [Theory]
    [InlineData("how do I close this tab?")]
    [InlineData("How do I open the export settings")]
    [InlineData("what does this button do?")]
    [InlineData("why is this red?")]
    [InlineData("where is the save option")]
    [InlineData("explain this screen")]
    [InlineData("teach me how to click through this")]
    [InlineData("show me where the settings are")]
    [InlineData("tell me what happens if I press that")]
    public void A_question_about_the_screen_is_not_a_command(string request) =>
        Assert.False(RequestIntent.IsComputerActionRequest(request), $"'{request}' should read as a question");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hello")]
    [InlineData("thanks, that worked")]
    public void Idle_chatter_is_not_a_command(string? request) =>
        Assert.False(RequestIntent.IsComputerActionRequest(request));

    [Theory]
    [InlineData("what is on my screen?")]
    [InlineData("where is the export button")]
    [InlineData("what does this do?")]
    public void Screen_questions_require_a_capture(string request) =>
        Assert.True(RequestIntent.RequiresScreenObservation(request));

    [Fact]
    public void A_general_question_does_not_require_a_capture() =>
        Assert.False(RequestIntent.RequiresScreenObservation("who wrote the odyssey"));
}
