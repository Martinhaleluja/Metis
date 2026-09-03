using Metis.Core.Services;
using Xunit;

namespace Metis.Tests;

/// <summary>
/// The privacy policy names Sentry and promises it never receives a key, a
/// token, or anything read off the screen. This is the code that keeps that
/// promise, so these are the tests that say it still does.
///
/// The strings below are shaped like real credentials and are not real ones.
/// </summary>
public sealed class SecretRedactionTests
{
    [Theory]
    // The leak that actually happens: a failed request stringified its own
    // Authorization header into the exception message.
    [InlineData("Unauthorized calling https://api.openai.com: Bearer sk-proj-A1b2C3d4E5f6G7h8I9j0K1l2M3n4")]
    [InlineData("Anthropic refused the key sk-ant-api03-QQQQWWWWEEEERRRRTTTTYYYY1234")]
    [InlineData("Gemini said 400 for AIzaSyA1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q")]
    [InlineData("supabase returned 401 for sb_secret_A1b2C3d4E5f6G7h8I9j0K1l2")]
    [InlineData("polar_oat_A1b2C3d4E5f6G7h8I9j0K1l2M3n4 was rejected")]
    [InlineData("webhook secret whsec_A1b2C3d4E5f6G7h8I9j0K1l2M3n4 did not verify")]
    public void A_key_shaped_string_never_survives(string message)
    {
        var cleaned = SecretRedaction.Apply(message, userName: "someone", homeDirectory: @"C:\Users\someone");

        Assert.Contains(SecretRedaction.Placeholder, cleaned);
        Assert.DoesNotContain("sk-proj-", cleaned);
        Assert.DoesNotContain("sk-ant-", cleaned);
        Assert.DoesNotContain("AIzaSy", cleaned);
        Assert.DoesNotContain("sb_secret_", cleaned);
        Assert.DoesNotContain("polar_oat_", cleaned);
        Assert.DoesNotContain("whsec_", cleaned);
    }

    [Fact]
    public void A_supabase_access_token_never_survives()
    {
        // Three base64 segments. This is the one a stack trace picks up from a
        // request to the gateway, and it grants the bearer someone's account.
        const string jwt =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"
            + ".eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ"
            + ".dQw4w9WgXcQdQw4w9WgXcQdQw4w9WgXcQdQw4w9WgXcQ";

        var cleaned = SecretRedaction.Apply($"GET /v1/me failed with {jwt}");

        Assert.DoesNotContain("eyJhbGciOi", cleaned);
        Assert.Contains(SecretRedaction.Placeholder, cleaned);
    }

    [Fact]
    public void The_home_directory_goes_before_the_username_inside_it()
    {
        // Replacing the name first would leave "C:\Users\<user>" behind, and
        // the home pattern would then no longer match. Order is the whole test.
        var cleaned = SecretRedaction.Apply(
            @"at Metis.App.Runtime.MetisRuntime.Ask() in C:\Users\martin\Documents\Lulu\src\App.cs:line 12",
            userName: "martin",
            homeDirectory: @"C:\Users\martin");

        Assert.DoesNotContain("martin", cleaned);
        Assert.Contains("<home>", cleaned);
        Assert.Contains("App.cs", cleaned);
    }

    [Fact]
    public void A_username_appears_nowhere_even_outside_a_path()
    {
        var cleaned = SecretRedaction.Apply(
            "Could not open the settings for martin",
            userName: "martin",
            homeDirectory: @"C:\Users\martin");

        Assert.DoesNotContain("martin", cleaned);
        Assert.Contains("<user>", cleaned);
    }

    [Fact]
    public void A_very_short_username_is_left_alone()
    {
        // Redacting a two-letter name would shred ordinary English on the way
        // past and make every report unreadable.
        var cleaned = SecretRedaction.Apply(
            "Could not read the file",
            userName: "al",
            homeDirectory: @"C:\Users\al");

        Assert.Equal("Could not read the file", cleaned);
    }

    [Fact]
    public void Ordinary_text_is_returned_unchanged()
    {
        // The point of a crash report is the crash. Over-redacting until
        // nothing is legible would be its own kind of failure.
        const string message = "Object reference not set to an instance of an object.";

        Assert.Equal(message, SecretRedaction.Apply(message, "someone", @"C:\Users\someone"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_gives_nothing_out(string? text)
    {
        Assert.Equal(string.Empty, SecretRedaction.Apply(text));
    }
}
