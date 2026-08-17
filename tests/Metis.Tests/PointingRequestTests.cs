using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// "Show me where X is" is answered by a mark on the screen. Recognising it is
/// what triggers Metis to find the control itself when the model replies with
/// prose and no coordinates.
/// </summary>
public sealed class PointingRequestTests
{
    [Theory]
    [InlineData("show me where the search bar is")]
    [InlineData("where is the save button")]
    [InlineData("where's the export option")]
    [InlineData("where can i type my email")]
    [InlineData("where do i put the title")]
    [InlineData("point to the settings icon")]
    [InlineData("highlight the toolbar")]
    [InlineData("which button do I press")]
    [InlineData("find the address bar")]
    [InlineData("Show me the input box")]
    public void A_request_to_be_shown_is_recognised(string request) =>
        Assert.True(RequestIntent.IsPointingRequest(request), $"'{request}' should ask to be shown");

    [Theory]
    [InlineData("what does this button do")]
    [InlineData("why is this red")]
    [InlineData("open chrome")]
    [InlineData("summarise this document")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_requests_are_not_pointing_requests(string? request) =>
        Assert.False(RequestIntent.IsPointingRequest(request));

    [Fact]
    public void A_pointing_request_is_not_treated_as_a_command_to_act()
    {
        // It asks to be shown, so Guide mode must point rather than click.
        Assert.False(RequestIntent.IsComputerActionRequest("show me where the delete button is"));
        Assert.True(RequestIntent.IsPointingRequest("show me where the delete button is"));
    }

    [Fact]
    public void A_pointing_request_still_requires_the_screen() =>
        Assert.True(RequestIntent.RequiresScreenObservation("show me where the search bar is"));
}
