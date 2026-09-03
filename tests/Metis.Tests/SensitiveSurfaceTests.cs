using Metis.Core.Agents.Browsing;

namespace Metis.Tests;

/// <summary>
/// The rule that stops an agent typing into a login or a payment form. Its two
/// failure directions are not equal: a false stop costs one interaction, a
/// false continue means an autonomous program filling in card details. So the
/// tests lean on the cases where it must stop.
/// </summary>
public sealed class SensitiveSurfaceTests
{
    [Fact]
    public void An_ordinary_page_is_not_sensitive()
    {
        var signals = new PageSignals(
            Url: "https://example.com/docs/getting-started",
            ButtonLabels: ["Next", "Copy", "Search"]);

        Assert.Equal(SensitiveKind.None, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_password_field_means_sign_in()
    {
        var signals = new PageSignals("https://example.com/login", HasPasswordField: true);

        Assert.Equal(SensitiveKind.SignIn, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_one_time_code_field_also_means_sign_in()
    {
        var signals = new PageSignals("https://example.com/verify", HasOneTimeCodeField: true);

        Assert.Equal(SensitiveKind.SignIn, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_new_password_field_means_signing_up_rather_than_in()
    {
        var signals = new PageSignals(
            "https://example.com/account",
            HasPasswordField: true,
            HasNewPasswordField: true);

        Assert.Equal(SensitiveKind.SignUp, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_card_field_means_payment()
    {
        var signals = new PageSignals("https://shop.example.com/basket", HasCardNumberField: true);

        Assert.Equal(SensitiveKind.Payment, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_payment_iframe_means_payment_even_with_no_visible_card_field()
    {
        // Stripe and PayPal put the real fields in an iframe, so the page
        // itself often has no card input to find.
        var signals = new PageSignals("https://shop.example.com/basket", HasPaymentFrame: true);

        Assert.Equal(SensitiveKind.Payment, SensitiveSurface.Detect(signals));
    }

    [Theory]
    [InlineData("https://shop.example.com/checkout")]
    [InlineData("https://shop.example.com/CHECKOUT/step-2")]
    [InlineData("https://example.com/billing/upgrade")]
    [InlineData("https://example.com/subscribe")]
    public void A_checkout_url_is_treated_as_payment(string url)
    {
        Assert.Equal(SensitiveKind.Payment, SensitiveSurface.Detect(new PageSignals(url)));
    }

    [Theory]
    [InlineData("Place order")]
    [InlineData("Pay now")]
    [InlineData("Complete purchase")]
    [InlineData("Buy Now")]
    public void A_button_that_spends_money_is_treated_as_payment(string label)
    {
        var signals = new PageSignals("https://example.com/thing", ButtonLabels: ["Cancel", label]);

        Assert.Equal(SensitiveKind.Payment, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_captcha_wins_over_everything_else_on_the_page()
    {
        // A CAPTCHA on a login page is still the question the agent must not
        // try to answer, so it has to be reported ahead of the login.
        var signals = new PageSignals(
            "https://example.com/login",
            HasPasswordField: true,
            HasCaptchaFrame: true);

        Assert.Equal(SensitiveKind.HumanCheck, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void A_sign_up_button_is_enough_on_its_own()
    {
        var signals = new PageSignals("https://example.com/", ButtonLabels: ["Create account", "Learn more"]);

        Assert.Equal(SensitiveKind.SignUp, SensitiveSurface.Detect(signals));
    }

    [Fact]
    public void Nothing_to_look_at_is_not_sensitive()
    {
        Assert.Equal(SensitiveKind.None, SensitiveSurface.Detect(null));
        Assert.Equal(SensitiveKind.None, SensitiveSurface.Detect(new PageSignals()));
    }

    [Theory]
    [InlineData(SensitiveKind.SignIn)]
    [InlineData(SensitiveKind.SignUp)]
    [InlineData(SensitiveKind.Payment)]
    [InlineData(SensitiveKind.HumanCheck)]
    public void Every_reason_to_stop_has_something_to_say_to_the_user(SensitiveKind kind)
    {
        // The user is being handed a browser mid-task; being told why is the
        // difference between a hand-over and an agent that just stopped.
        Assert.False(string.IsNullOrWhiteSpace(SensitiveSurface.Explain(kind)));
    }
}
