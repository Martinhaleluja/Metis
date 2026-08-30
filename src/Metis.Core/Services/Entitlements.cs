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
    /// Whether an account has a capability.
    ///
    /// <paramref name="billingIsLive"/> used to be a compile-time constant here,
    /// which meant the day Metis started charging was the day every installed
    /// copy needed replacing before it agreed with the server about what its
    /// user had bought. It is a parameter now, sourced from the
    /// <c>billing_state</c> row and carried to the client inside an
    /// <see cref="EntitlementSnapshot"/>, so the switch is one SQL update.
    ///
    /// There is deliberately no overload that omits it. A default would let a
    /// call site quietly assume the friendlier answer, and the friendlier answer
    /// here is the one that gives things away.
    /// </summary>
    public static bool Has(MetisAccount account, MetisFeature feature, bool billingIsLive)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Staff capabilities are about who you are, not what you paid, so they
        // are decided the same way whether or not billing is live.
        var isStaffOnly = feature is
            MetisFeature.DeveloperMode or MetisFeature.ExperimentalFeatures or
            MetisFeature.StagingAccess or MetisFeature.InternalCostVisibility or
            MetisFeature.AdminDashboard;

        // While nobody can buy anything, every paid capability is free, to
        // everyone, signed in or not. That is not a placeholder to be tidied
        // away later — it is the only honest state while there is no way to take
        // payment, and pretending otherwise would restrict a product nobody can
        // currently buy.
        //
        // It also protects something easy to lose. Metis works entirely without
        // an account, on the user's own API key, and adding paid plans must not
        // quietly take features away from people who never asked for one.
        if (!billingIsLive && !isStaffOnly)
        {
            return true;
        }

        // A signed-out copy of Metis still works with local providers and with
        // the user's own key, but it has no account to carry entitlements, so it
        // gets none of them. Neither of those paths reaches the gateway, so
        // neither costs Metis anything either.
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
            // ---- Everyone with an account ----
            MetisFeature.ComputerControl => true,
            MetisFeature.ManagedAiRouting => true,
            MetisFeature.UsageVisibility => true,

            // ---- Plus and above ----
            MetisFeature.ManagedPremiumModels => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,
            MetisFeature.ManagedScreenVision => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,
            MetisFeature.AdvancedAutomation => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,
            MetisFeature.AutonomousAgents => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,
            MetisFeature.PersistentMemory => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,
            MetisFeature.BrowserAssistance => account.IsAtLeast(PlanTier.Plus) || account.IsStaff,

            // ---- Pro only ----
            MetisFeature.CustomAiProvider => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,
            MetisFeature.SystemCommands => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,
            MetisFeature.AdvancedAgents => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,
            MetisFeature.ProviderManagement => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,

            // ---- Staff ----
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
    /// Every capability an account holds, so building an
    /// <see cref="EntitlementSnapshot"/> does not mean each caller re-deriving
    /// the set and getting a slightly different one.
    /// </summary>
    public static IReadOnlySet<MetisFeature> GrantedFeatures(MetisAccount account, bool billingIsLive)
    {
        ArgumentNullException.ThrowIfNull(account);
        return Enum.GetValues<MetisFeature>()
            .Where(feature => Has(account, feature, billingIsLive))
            .ToHashSet();
    }

    /// <summary>
    /// Why a capability was refused, for telling the user something better than
    /// nothing happening. A refusal nobody can read is indistinguishable from a
    /// bug, and this is the sentence that separates them.
    /// </summary>
    public static string Explain(MetisAccount account, MetisFeature feature, bool billingIsLive)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (Has(account, feature, billingIsLive))
        {
            return billingIsLive ? "Available on this account." : "Free while Metis is in early access.";
        }

        if (!account.IsSignedIn)
        {
            return "Sign in to use this, or add your own API key in Setup.";
        }

        if (!account.EmailVerified)
        {
            return "Verify your email address to use this.";
        }

        return feature switch
        {
            // Every sentence here has to end somewhere the user can act. The
            // ones about managed AI say what still works on their own key,
            // because "your plan is too small" with no way forward reads as a
            // wall rather than a choice.
            MetisFeature.ManagedScreenVision =>
                "Metis reading your screen on its own AI is part of Plus. Your own API key still can.",
            MetisFeature.ManagedPremiumModels =>
                "Models beyond Gemini on Metis's AI are part of Plus.",
            MetisFeature.AdvancedAutomation =>
                "Advanced automation is part of Plus.",
            MetisFeature.AutonomousAgents =>
                "Background agents are part of Plus.",
            MetisFeature.PersistentMemory =>
                "Memory beyond the free allowance is part of Plus.",
            MetisFeature.BrowserAssistance =>
                "Browser assistance is part of Plus.",

            MetisFeature.AdvancedAgents =>
                "Multi-agent workflows are part of Metis Pro.",
            MetisFeature.ProviderManagement =>
                "Choosing your own models and endpoints is part of Metis Pro.",
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

    /// <summary>
    /// Reads a plan sent by the backend.
    ///
    /// Only the literal word "pro" earns Pro. The looser words that used to mean
    /// Pro — "paid", "active" — are really subscription *statuses* rather than
    /// plan names, and once there are two paid plans they no longer say which
    /// one was bought. They resolve to Plus, the smaller of the two, on the same
    /// principle <see cref="ParseRole"/> and <see cref="ParseEnvironment"/>
    /// already follow: a value that arrives ambiguous must not be the reason
    /// someone gets more than they paid for.
    /// </summary>
    public static PlanTier ParsePlan(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pro" => PlanTier.Pro,
        "plus" or "paid" or "active" => PlanTier.Plus,
        _ => PlanTier.Free
    };

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
