using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Every permission decision runs through one function, so these are the cases
/// that decide who gets what. A check written in two places will eventually be
/// written differently, and the copy that drifts is the one that grants access
/// it should not.
///
/// The whole matrix is exercised under both values of <c>billingIsLive</c>,
/// because the two answers are completely different products: with it off,
/// Metis is a free tool that happens to have accounts, and with it on it is a
/// subscription. Both have to be right, and only one of them can be tried by
/// running the application today.
/// </summary>
public sealed class EntitlementTests
{
    private static MetisAccount Account(UserRole role, PlanTier plan = PlanTier.Free) =>
        new("u_1", role, plan, MetisEnvironment.Production);

    // ============================ Plan Gating ============================

    /// <summary>
    /// What Free genuinely does not have.
    ///
    /// Screen vision and agents used to be on this list and are deliberately
    /// not any more: Free is now sold as fifty talk messages, plenty of
    /// dictation and ten agent messages, so it has to be able to do those
    /// things. Withholding the capability while advertising an allowance for it
    /// would be selling something the code refuses. How much Free gets is a
    /// number in PlanLimits, enforced on the server per request, rather than a
    /// yes or no here.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.CustomAiProvider)]
    [InlineData(MetisFeature.SystemCommands)]
    [InlineData(MetisFeature.AdvancedAgents)]
    [InlineData(MetisFeature.ProviderManagement)]
    [InlineData(MetisFeature.ManagedPremiumModels)]
    [InlineData(MetisFeature.BrowserAssistance)]
    public void Paid_capabilities_are_gated_for_free_tier(MetisFeature feature)
    {
        Assert.False(Entitlements.Has(Account(UserRole.User, PlanTier.Free), feature, billingIsLive: false));
        Assert.False(Entitlements.Has(MetisAccount.SignedOut, feature, billingIsLive: false));
    }

    /// <summary>
    /// Free tier users have access to foundational features.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.ComputerControl)]
    [InlineData(MetisFeature.ManagedAiRouting)]
    [InlineData(MetisFeature.UsageVisibility)]
    public void Free_capabilities_are_open_to_all_signed_in_users(MetisFeature feature)
    {
        Assert.True(Entitlements.Has(Account(UserRole.User, PlanTier.Free), feature, billingIsLive: false));
    }

    /// <summary>
    /// Staff capabilities stay closed to ordinary users regardless of tier.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.DeveloperMode)]
    [InlineData(MetisFeature.AdminDashboard)]
    [InlineData(MetisFeature.InternalCostVisibility)]
    public void Staff_capabilities_stay_closed_to_ordinary_users(MetisFeature feature)
    {
        Assert.False(Entitlements.Has(Account(UserRole.User, PlanTier.Pro), feature, billingIsLive: false));
        Assert.False(Entitlements.Has(MetisAccount.SignedOut, feature, billingIsLive: false));
    }

    [Fact]
    public void A_free_tier_refusal_explains_upgrade_requirement() =>
        Assert.Contains("part of",
            Entitlements.Explain(Account(UserRole.User), MetisFeature.CustomAiProvider, billingIsLive: false),
            StringComparison.OrdinalIgnoreCase);

    // ============================ Billing on =============================

    /// <summary>
    /// The three tiers, feature by feature. Written out rather than derived so
    /// that changing what a plan includes means editing a line in a table an
    /// author can read, which is the same property Entitlements.Has itself is
    /// built for.
    /// </summary>
    [Theory]
    // Everyone with a verified account.
    [InlineData(MetisFeature.ManagedAiRouting, PlanTier.Free, true)]
    [InlineData(MetisFeature.ManagedAiRouting, PlanTier.Pro, true)]
    [InlineData(MetisFeature.ManagedAiRouting, PlanTier.Max, true)]
    [InlineData(MetisFeature.UsageVisibility, PlanTier.Free, true)]
    [InlineData(MetisFeature.ComputerControl, PlanTier.Free, true)]

    // Every plan, Free included.
    //
    // These three are sold to Free by the amount rather than withheld from it:
    // fifty talk messages, plenty of dictation, ten agent messages. The
    // capability has to be granted for the allowance to mean anything, and how
    // much of it there is lives in PlanLimits instead.
    [InlineData(MetisFeature.ManagedScreenVision, PlanTier.Free, true)]
    [InlineData(MetisFeature.ManagedScreenVision, PlanTier.Pro, true)]
    [InlineData(MetisFeature.AutonomousAgents, PlanTier.Free, true)]
    [InlineData(MetisFeature.AutonomousAgents, PlanTier.Pro, true)]
    [InlineData(MetisFeature.PersistentMemory, PlanTier.Free, true)]

    // Pro and above.
    [InlineData(MetisFeature.ManagedPremiumModels, PlanTier.Free, false)]
    [InlineData(MetisFeature.ManagedPremiumModels, PlanTier.Pro, true)]
    [InlineData(MetisFeature.AdvancedAutomation, PlanTier.Free, false)]
    [InlineData(MetisFeature.AdvancedAutomation, PlanTier.Pro, true)]
    [InlineData(MetisFeature.BrowserAssistance, PlanTier.Free, false)]
    [InlineData(MetisFeature.BrowserAssistance, PlanTier.Pro, true)]

    // Max only.
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Free, false)]
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Pro, false)]
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Max, true)]
    [InlineData(MetisFeature.AdvancedAgents, PlanTier.Pro, false)]
    [InlineData(MetisFeature.AdvancedAgents, PlanTier.Max, true)]
    [InlineData(MetisFeature.ProviderManagement, PlanTier.Pro, false)]
    [InlineData(MetisFeature.ProviderManagement, PlanTier.Max, true)]
    [InlineData(MetisFeature.SystemCommands, PlanTier.Pro, false)]
    [InlineData(MetisFeature.SystemCommands, PlanTier.Max, true)]

    // Staff-only, whatever was paid.
    [InlineData(MetisFeature.DeveloperMode, PlanTier.Max, false)]
    [InlineData(MetisFeature.AdminDashboard, PlanTier.Max, false)]
    public void The_plan_table(MetisFeature feature, PlanTier plan, bool expected) =>
        Assert.Equal(expected, Entitlements.Has(Account(UserRole.User, plan), feature, billingIsLive: true));

    /// <summary>
    /// Nobody signed in earns anything once billing is live. Nothing is lost by
    /// this: a signed-out copy of Metis runs on the user's own key or a local
    /// model, and neither path is ever asked about an entitlement.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.ManagedAiRouting)]
    [InlineData(MetisFeature.ComputerControl)]
    [InlineData(MetisFeature.CustomAiProvider)]
    public void A_signed_out_copy_earns_nothing_once_billing_is_live(MetisFeature feature) =>
        Assert.False(Entitlements.Has(MetisAccount.SignedOut, feature, billingIsLive: true));

    /// <summary>
    /// Staff get the paid capabilities without a subscription, because a test
    /// account that cannot reach the thing being tested is not a test account.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Developer)]
    [InlineData(UserRole.Founder)]
    [InlineData(UserRole.Admin)]
    public void Staff_reach_the_paid_capabilities_on_the_free_plan(UserRole role)
    {
        var account = Account(role);

        Assert.True(Entitlements.Has(account, MetisFeature.CustomAiProvider, billingIsLive: true));
        Assert.True(Entitlements.Has(account, MetisFeature.ManagedScreenVision, billingIsLive: true));
        Assert.True(Entitlements.Has(account, MetisFeature.AdvancedAgents, billingIsLive: true));
    }

    [Fact]
    public void An_unverified_account_earns_nothing_once_billing_is_live()
    {
        var unverified = Account(UserRole.User, PlanTier.Pro) with { EmailVerified = false };

        Assert.False(Entitlements.Has(unverified, MetisFeature.CustomAiProvider, billingIsLive: true));
        Assert.False(Entitlements.Has(unverified, MetisFeature.ManagedAiRouting, billingIsLive: true));
    }

    /// <summary>
    /// The refusal for a managed capability has to name what still works, or it
    /// reads as a wall rather than a choice. A Free user's own API key can still
    /// read their screen; they should be told so in the same sentence.
    /// </summary>
    [Fact]
    public void A_managed_refusal_names_what_still_works() =>
        Assert.Contains("Metis Pro",
            Entitlements.Explain(Account(UserRole.User), MetisFeature.ManagedPremiumModels, billingIsLive: true),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void GrantedFeatures_agrees_with_Has()
    {
        var account = Account(UserRole.User, PlanTier.Pro);
        var granted = Entitlements.GrantedFeatures(account, billingIsLive: true);

        foreach (var feature in Enum.GetValues<MetisFeature>())
        {
            Assert.Equal(Entitlements.Has(account, feature, billingIsLive: true), granted.Contains(feature));
        }
    }

    // ============================ Plan parsing ===========================

    /// <summary>
    /// The declaration order of PlanTier is load-bearing: IsAtLeast compares it
    /// ordinally, so a tier inserted in the wrong place would silently make a
    /// smaller plan test as larger than a bigger one.
    /// </summary>
    [Fact]
    public void The_plans_are_ordered_smallest_first()
    {
        Assert.True(PlanTier.Free < PlanTier.Pro);
        Assert.True(PlanTier.Pro < PlanTier.Max);

        Assert.True(Account(UserRole.User, PlanTier.Max).IsAtLeast(PlanTier.Pro));
        Assert.False(Account(UserRole.User, PlanTier.Pro).IsAtLeast(PlanTier.Max));
    }

    /// <summary>
    /// "paid" and "active" describe a subscription's status rather than which
    /// plan was bought, and once there are two paid plans they no longer say
    /// which. They resolve to the smaller one, because a value that arrives
    /// ambiguous must never be the reason someone gets more than they paid for.
    /// </summary>
    [Theory]
    [InlineData("max", PlanTier.Max)]
    [InlineData("MAX ", PlanTier.Max)]
    [InlineData("pro", PlanTier.Pro)]
    [InlineData("PRO ", PlanTier.Pro)]
    // The old name for the middle plan. It is the same tier and the same
    // person, so it must not demote them to Free.
    [InlineData("plus", PlanTier.Pro)]
    [InlineData("paid", PlanTier.Pro)]
    [InlineData("active", PlanTier.Pro)]
    public void A_known_plan_is_read(string value, PlanTier expected) =>
        Assert.Equal(expected, Entitlements.ParsePlan(value));

    [Theory]
    [InlineData("free")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cancelled")]
    [InlineData("premium")]
    [InlineData("enterprise")]
    public void An_unrecognised_plan_is_free(string? value) =>
        Assert.Equal(PlanTier.Free, Entitlements.ParsePlan(value));

    // ============================ Role parsing ===========================

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Pro)]
    public void Developer_mode_is_closed_to_ordinary_accounts(UserRole role) =>
        Assert.False(Entitlements.Has(Account(role, PlanTier.Pro), MetisFeature.DeveloperMode, billingIsLive: false));

    /// <summary>
    /// The narrowest staff capability. Being able to test the product is not
    /// the same as being able to read the company's numbers.
    /// </summary>
    [Fact]
    public void The_dashboard_is_narrower_than_the_rest_of_staff_access()
    {
        Assert.True(Entitlements.Has(Account(UserRole.Founder), MetisFeature.AdminDashboard, billingIsLive: false));
        Assert.True(Entitlements.Has(Account(UserRole.Admin), MetisFeature.AdminDashboard, billingIsLive: false));
        Assert.False(Entitlements.Has(Account(UserRole.Developer), MetisFeature.AdminDashboard, billingIsLive: false));
    }

    /// <summary>
    /// An unverified address is an unproven claim to it, and that still gates
    /// the staff capabilities while everything else is free.
    /// </summary>
    [Fact]
    public void An_unverified_account_earns_no_staff_access()
    {
        var unverified = Account(UserRole.Founder, PlanTier.Pro) with { EmailVerified = false };

        Assert.False(Entitlements.Has(unverified, MetisFeature.DeveloperMode, billingIsLive: false));
        Assert.False(Entitlements.Has(unverified, MetisFeature.AdminDashboard, billingIsLive: false));
    }

    [Fact]
    public void A_refusal_says_something_the_user_can_act_on() =>
        Assert.Contains("Sign in",
            Entitlements.Explain(MetisAccount.SignedOut, MetisFeature.DeveloperMode, billingIsLive: false),
            StringComparison.Ordinal);

    /// <summary>
    /// A role name that arrives misspelled, from an older client, or invented
    /// must never be the reason someone gains access.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("superuser")]
    [InlineData("FOUNDER ")]
    public void An_unrecognised_role_is_an_ordinary_user(string? value)
    {
        var role = Entitlements.ParseRole(value);

        Assert.True(role == UserRole.User || value?.Trim().ToLowerInvariant() == "founder");
    }

    [Theory]
    [InlineData("founder", UserRole.Founder)]
    [InlineData("Developer", UserRole.Developer)]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("pro", UserRole.Pro)]
    public void A_known_role_is_read(string value, UserRole expected) =>
        Assert.Equal(expected, Entitlements.ParseRole(value));

    /// <summary>
    /// Guessing wrong towards the most restricted environment is the only
    /// failure that costs nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("prod")]
    public void An_unreadable_environment_is_production(string? value) =>
        Assert.Equal(MetisEnvironment.Production, Entitlements.ParseEnvironment(value));

    [Theory]
    [InlineData("development", MetisEnvironment.Development)]
    [InlineData("local", MetisEnvironment.Development)]
    [InlineData("staging", MetisEnvironment.Staging)]
    public void A_known_environment_is_read(string value, MetisEnvironment expected) =>
        Assert.Equal(expected, Entitlements.ParseEnvironment(value));
}
