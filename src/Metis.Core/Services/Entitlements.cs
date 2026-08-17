using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// The one place that decides what an account may do.
///
/// The specification asks for this explicitly, and the reason is worth stating:
/// a permission check written in two places will eventually be written
/// differently, and the copy that drifts is the one that grants access it
/// should not. Adding a plan later should mean editing the table below rather
/// than auditing every call site.
///
/// This is the client's copy of the rules, and it decides what to *show*. It
/// never decides what may actually happen: the same entitlement is checked
/// again on the server before anything is done, because a desktop application
/// can be edited by whoever runs it and anything it claims about itself is a
/// request rather than a fact.
/// </summary>
public static class Entitlements
{
    /// <summary>
    /// Whether Metis is charging anyone yet.
    ///
    /// While this is false every paid capability is free, to everyone, signed
    /// in or not. That is not a placeholder to be tidied away later — it is the
    /// only honest state while there is no way to take payment, and pretending
    /// otherwise would restrict a product nobody can currently buy.
    ///
    /// It also protects something easy to lose. Metis today works entirely
    /// without an account, on the user's own API key, and adding sign-in must
    /// not quietly take features away from people who never asked for one.
    ///
    /// Flipping this to true turns the plan checks back on in one place. The
    /// role checks below are unaffected either way: free does not mean everyone
    /// is staff.
    /// </summary>
    public const bool BillingIsLive = false;

    /// <summary>
    /// Whether an account has a capability.
    /// </summary>
    public static bool Has(MetisAccount account, MetisFeature feature)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Staff capabilities are about who you are, not what you paid, so they
        // are decided the same way whether or not billing is live.
        var isStaffOnly = feature is
            MetisFeature.DeveloperMode or MetisFeature.ExperimentalFeatures or
            MetisFeature.StagingAccess or MetisFeature.InternalCostVisibility or
            MetisFeature.AdminDashboard;

        if (!BillingIsLive && !isStaffOnly)
        {
            return true;
        }

        // A signed-out copy of Metis still works with local providers, but it
        // has no account to carry entitlements, so it gets none of them.
        if (!account.IsSignedIn)
        {
            return false;
        }

        // An unverified email is an unproven claim to an address. Until it is
        // verified the account exists but earns nothing beyond the basics.
        if (!account.EmailVerified)
        {
            return false;
        }

        return feature switch
        {
            MetisFeature.CustomAiProvider => account.Plan == PlanTier.Pro || account.IsStaff,
            MetisFeature.ComputerControl => true,
            MetisFeature.SystemCommands => account.Plan == PlanTier.Pro || account.IsStaff,

            MetisFeature.DeveloperMode => account.IsStaff,
            MetisFeature.ExperimentalFeatures => account.IsStaff,
            MetisFeature.StagingAccess => account.IsStaff,
            MetisFeature.InternalCostVisibility => account.IsStaff,

            // The narrowest of the staff capabilities: seeing the company's
            // numbers is not the same as being able to test its features.
            MetisFeature.AdminDashboard => account.Role is UserRole.Founder or UserRole.Admin,

            _ => false
        };
    }

    /// <summary>
    /// Why a capability was refused, for telling the user something better than
    /// nothing happening. A refusal nobody can read is indistinguishable from a
    /// bug, and this is the sentence that separates them.
    /// </summary>
    public static string Explain(MetisAccount account, MetisFeature feature)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (Has(account, feature))
        {
            return BillingIsLive ? "Available on this account." : "Free while Metis is in early access.";
        }

        if (!account.IsSignedIn)
        {
            return "Sign in to use this.";
        }

        if (!account.EmailVerified)
        {
            return "Verify your email address to use this.";
        }

        return feature switch
        {
            MetisFeature.CustomAiProvider =>
                "Connecting your own AI provider is part of Metis Pro.",
            MetisFeature.SystemCommands =>
                "Running system commands is part of Metis Pro.",
            MetisFeature.AdminDashboard =>
                "This dashboard is internal to Metis.",
            _ => "This is not available on your account."
        };
    }

    /// <summary>
    /// Reads a role sent by the backend. Anything unrecognised becomes
    /// <see cref="UserRole.User"/> — a role name that arrives misspelled, from
    /// an older client, or invented must never be the reason someone gains
    /// access.
    /// </summary>
    public static UserRole ParseRole(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "admin" => UserRole.Admin,
        "founder" => UserRole.Founder,
        "developer" or "dev" => UserRole.Developer,
        "pro" => UserRole.Pro,
        _ => UserRole.User
    };

    public static PlanTier ParsePlan(string? value) =>
        value?.Trim().ToLowerInvariant() is "pro" or "paid" or "active"
            ? PlanTier.Pro
            : PlanTier.Free;

    /// <summary>
    /// Reads the environment a build is pointed at, from configuration rather
    /// than from anything the signed-in user can influence. Unrecognised values
    /// resolve to Production because it is the most restricted of the three.
    /// </summary>
    public static MetisEnvironment ParseEnvironment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "development" or "dev" or "local" => MetisEnvironment.Development,
        "staging" or "stage" or "beta" => MetisEnvironment.Staging,
        _ => MetisEnvironment.Production
    };
}
