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

        // A signed-out copy of Metis still works with local providers and with
        // the user's own key, but it has no account to carry entitlements, so it
        // gets none of them.
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

            // ---- Every plan, Free included ----
            //
            // Agents are here rather than behind Pro because Free is sold with
            // ten agent messages a month. The plan buys an amount, not a
            // permission: refusing the capability outright and then quoting an
            // allowance for it on the pricing page would be selling something
            // the code will not do. How many is PlanLimits.MaxAgentStepsPerMonth,
            // checked on the server for every step.
            MetisFeature.AutonomousAgents => true,
            MetisFeature.ManagedScreenVision => true,
            MetisFeature.PersistentMemory => true,

            // ---- Pro and above ----
            MetisFeature.ManagedPremiumModels => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,
            MetisFeature.AdvancedAutomation => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,
            MetisFeature.BrowserAssistance => account.IsAtLeast(PlanTier.Pro) || account.IsStaff,

            // ---- Max only ----
            MetisFeature.CustomAiProvider => account.IsAtLeast(PlanTier.Max) || account.IsStaff,
            MetisFeature.SystemCommands => account.IsAtLeast(PlanTier.Max) || account.IsStaff,
            MetisFeature.AdvancedAgents => account.IsAtLeast(PlanTier.Max) || account.IsStaff,
            MetisFeature.ProviderManagement => account.IsAtLeast(PlanTier.Max) || account.IsStaff,

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
            return "Available on this account.";
        }

        if (!account.IsSignedIn)
        {
            return "Sign in to use this, or choose a plan.";
        }

        if (!account.EmailVerified)
        {
            return "Verify your email address to use this.";
        }

        return feature switch
        {
            // Three of these are no longer refusals of the capability at all —
            // screen vision, agents and memory are on every plan, sold by the
            // amount rather than withheld. Their sentences stay because Explain
            // is also reached when the whole account is unverified or signed
            // out, and because a plan added later might narrow them again.
            MetisFeature.ManagedScreenVision =>
                "Reading your screen in full detail is part of Metis Pro.",
            MetisFeature.ManagedPremiumModels =>
                "The larger AI models are part of Metis Pro.",
            MetisFeature.AdvancedAutomation =>
                "Advanced automation is part of Metis Pro.",
            MetisFeature.AutonomousAgents =>
                "Free includes ten agent messages a month. Metis Pro includes four hundred.",
            MetisFeature.PersistentMemory =>
                "Remembering more of what you are working on is part of Metis Pro.",
            MetisFeature.BrowserAssistance =>
                "Help with what is in your browser is part of Metis Pro.",

            MetisFeature.AdvancedAgents =>
                "Agents that hand work to each other are part of Metis Max.",
            MetisFeature.ProviderManagement =>
                "Choosing your own models and endpoints is part of Metis Max.",
            MetisFeature.CustomAiProvider =>
                "Answering on your own AI account — OpenAI, Anthropic, Gemini or OpenRouter — is part of Metis Max.",
            MetisFeature.SystemCommands =>
                "Running background system tools is part of Metis Max.",

            MetisFeature.AdminDashboard =>
                "This dashboard is internal to Metis.",
            _ => "This feature is not included in your current plan."
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
    /// Only the literal word "max" earns Max. The looser words that used to mean
    /// a paid plan — "paid", "active" — are really subscription *statuses*
    /// rather than plan names, and with two paid plans they no longer say which
    /// one was bought. They resolve to Pro, the smaller of the two, on the same
    /// principle <see cref="ParseRole"/> and <see cref="ParseEnvironment"/>
    /// already follow: a value that arrives ambiguous must not be the reason
    /// someone gets more than they paid for.
    ///
    /// "plus" is the old name for the middle plan and resolves to Pro, which is
    /// what that plan is called now — the same tier, the same person, a
    /// different word. It is kept because the Postgres enum still carries the
    /// value and an old row must not silently demote its owner to Free.
    /// </summary>
    public static PlanTier ParsePlan(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "max" => PlanTier.Max,
        "pro" or "plus" or "paid" or "active" => PlanTier.Pro,
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
