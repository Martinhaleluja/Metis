namespace Metis.Core.Agents.Browsing;

/// <summary>What kind of thing the page is asking for.</summary>
public enum SensitiveKind
{
    None,

    /// <summary>A password or one-time code. Signing in.</summary>
    SignIn,

    /// <summary>Creating an account.</summary>
    SignUp,

    /// <summary>Card details, or a checkout.</summary>
    Payment,

    /// <summary>A CAPTCHA or other are-you-human check.</summary>
    HumanCheck
}

/// <summary>
/// What a page looks like, as far as the hand-over rule needs to know.
///
/// A plain record so the decision can be tested without a browser. The browser
/// fills it in by looking at the live page; this file only decides what it
/// means.
/// </summary>
public sealed record PageSignals(
    string Url = "",
    bool HasPasswordField = false,
    bool HasNewPasswordField = false,
    bool HasCardNumberField = false,
    bool HasOneTimeCodeField = false,
    bool HasCaptchaFrame = false,
    bool HasPaymentFrame = false,
    IReadOnlyList<string>? ButtonLabels = null)
{
    public IReadOnlyList<string> Buttons => ButtonLabels ?? [];
}

/// <summary>
/// Decides when an agent must stop and give the browser back to the person.
///
/// This is the rule the user asked for in as many words: the agent works up to
/// the login, the sign-up, or the payment, and then hands over. They type the
/// password; the agent carries on from the same page.
///
/// It is written as a pure function of what the page looks like, rather than as
/// a list of blocked websites, because the thing that matters is what is being
/// asked for and not who is asking. A login form is a login form on any domain,
/// including one nobody thought to put on a list.
///
/// A human check hands over too, and is never worked around. That is a
/// deliberate limit rather than a missing feature: a site putting a CAPTCHA up
/// is asking whether there is a person there, and the honest answer when an
/// agent is driving is no.
///
/// It errs towards stopping. A false stop costs the user one interaction; a
/// false continue means an agent typing into a payment form, and those are not
/// the same size of mistake.
/// </summary>
public static class SensitiveSurface
{
    public static SensitiveKind Detect(PageSignals? signals)
    {
        if (signals is null)
        {
            return SensitiveKind.None;
        }

        // Checked first. A CAPTCHA sitting on a login page is still a question
        // for the human, and it is the one the agent must not try to answer.
        if (signals.HasCaptchaFrame)
        {
            return SensitiveKind.HumanCheck;
        }

        if (signals.HasCardNumberField || signals.HasPaymentFrame || LooksLikeCheckout(signals))
        {
            return SensitiveKind.Payment;
        }

        // A new-password field is what distinguishes registering from signing
        // in, and the two deserve different sentences to the user.
        if (signals.HasNewPasswordField || MentionsSignUp(signals))
        {
            return SensitiveKind.SignUp;
        }

        if (signals.HasPasswordField || signals.HasOneTimeCodeField)
        {
            return SensitiveKind.SignIn;
        }

        return SensitiveKind.None;
    }

    /// <summary>What to tell the user when handing the browser over.</summary>
    public static string Explain(SensitiveKind kind) => kind switch
    {
        SensitiveKind.SignIn =>
            "This page is asking to sign in. I've stopped here — sign in yourself and I'll carry on from where you leave it.",
        SensitiveKind.SignUp =>
            "This page wants to create an account. That should be you rather than me — fill it in and I'll continue afterwards.",
        SensitiveKind.Payment =>
            "This is a payment page. I don't enter card details or confirm purchases. Take it from here and I'll pick up after.",
        SensitiveKind.HumanCheck =>
            "There's a check here asking whether a person is present. That's you, not me — complete it and I'll go on.",
        _ => string.Empty
    };

    private static bool LooksLikeCheckout(PageSignals signals)
    {
        var url = signals.Url ?? string.Empty;

        foreach (var marker in CheckoutUrlMarkers)
        {
            if (url.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var label in signals.Buttons)
        {
            foreach (var marker in PaymentButtonMarkers)
            {
                if (label.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MentionsSignUp(PageSignals signals)
    {
        foreach (var label in signals.Buttons)
        {
            foreach (var marker in SignUpButtonMarkers)
            {
                if (label.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly string[] CheckoutUrlMarkers =
    [
        "/checkout", "/payment", "/billing", "/purchase", "/subscribe", "/upgrade", "/cart/pay"
    ];

    private static readonly string[] PaymentButtonMarkers =
    [
        "place order", "pay now", "complete purchase", "confirm payment",
        "buy now", "proceed to payment", "checkout"
    ];

    private static readonly string[] SignUpButtonMarkers =
    [
        "sign up", "create account", "register", "get started free", "join now"
    ];
}
