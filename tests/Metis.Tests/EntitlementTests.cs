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

    // ============================ Billing off ============================

    /// <summary>
    /// Metis is free until there is a way to take payment. Every paid
    /// capability is open to everyone, and that includes people with no
    /// account at all — adding sign-in must not take features away from a
    /// product that has always worked without one.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.CustomAiProvider)]
    [InlineData(MetisFeature.SystemCommands)]
    [InlineData(MetisFeature.ComputerControl)]
    [InlineData(MetisFeature.ManagedScreenVision)]
    [InlineData(MetisFeature.AdvancedAgents)]
    [InlineData(MetisFeature.ProviderManagement)]
    public void Paid_capabilities_are_free_while_billing_is_off(MetisFeature feature)
    {
        Assert.True(Entitlements.Has(Account(UserRole.User), feature, billingIsLive: false));
        Assert.True(Entitlements.Has(MetisAccount.SignedOut, feature, billingIsLive: false));
    }

    /// <summary>
    /// Free does not mean everyone is staff. The role checks are the half that
    /// does not move when billing is switched off.
    /// </summary>
    [Theory]
    [InlineData(MetisFeature.DeveloperMode)]
    [InlineData(MetisFeature.AdminDashboard)]
    [InlineData(MetisFeature.InternalCostVisibility)]
    public void Staff_capabilities_stay_closed_while_everything_is_free(MetisFeature feature)
    {
        Assert.False(Entitlements.Has(Account(UserRole.User, PlanTier.Pro), feature, billingIsLive: false));
        Assert.False(Entitlements.Has(MetisAccount.SignedOut, feature, billingIsLive: false));
    }

    [Fact]
    public void A_free_capability_says_why_it_is_free() =>
        Assert.Contains("early access",
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
    [InlineData(MetisFeature.ManagedAiRouting, PlanTier.Plus, true)]
    [InlineData(MetisFeature.ManagedAiRouting, PlanTier.Pro, true)]
    [InlineData(MetisFeature.UsageVisibility, PlanTier.Free, true)]
    [InlineData(MetisFeature.ComputerControl, PlanTier.Free, true)]

    // Plus and above.
    [InlineData(MetisFeature.ManagedScreenVision, PlanTier.Free, false)]
    [InlineData(MetisFeature.ManagedScreenVision, PlanTier.Plus, true)]
    [InlineData(MetisFeature.ManagedScreenVision, PlanTier.Pro, true)]
    [InlineData(MetisFeature.ManagedPremiumModels, PlanTier.Free, false)]
    [InlineData(MetisFeature.ManagedPremiumModels, PlanTier.Plus, true)]
    [InlineData(MetisFeature.AdvancedAutomation, PlanTier.Free, false)]
    [InlineData(MetisFeature.AdvancedAutomation, PlanTier.Plus, true)]
    [InlineData(MetisFeature.AutonomousAgents, PlanTier.Free, false)]
    [InlineData(MetisFeature.AutonomousAgents, PlanTier.Plus, true)]
    [InlineData(MetisFeature.PersistentMemory, PlanTier.Plus, true)]
    [InlineData(MetisFeature.BrowserAssistance, PlanTier.Plus, true)]

    // Pro only.
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Free, false)]
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Plus, false)]
    [InlineData(MetisFeature.CustomAiProvider, PlanTier.Pro, true)]
    [InlineData(MetisFeature.AdvancedAgents, PlanTier.Plus, false)]
    [InlineData(MetisFeature.AdvancedAgents, PlanTier.Pro, true)]
    [InlineData(MetisFeature.ProviderManagement, PlanTier.Plus, false)]
    [InlineData(MetisFeature.ProviderManagement, PlanTier.Pro, true)]
    [InlineData(MetisFeature.SystemCommands, PlanTier.Plus, false)]
    [InlineData(MetisFeature.SystemCommands, PlanTier.Pro, true)]

    // Staff-only, whatever was paid.
    [InlineData(MetisFeature.DeveloperMode, PlanTier.Pro, false)]
    [InlineData(MetisFeature.AdminDashboard, PlanTier.Pro, false)]
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
        Assert.Contains("own API key",
            Entitlements.Explain(Account(UserRole.User), MetisFeature.ManagedScreenVision, billingIsLive: true),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void GrantedFeatures_agrees_with_Has()
    {
        var account = Account(UserRole.User, PlanTier.Plus);
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
        Assert.True(PlanTier.Free < PlanTier.Plus);
        Assert.True(PlanTier.Plus < PlanTier.Pro);

        Assert.True(Account(UserRole.User, PlanTier.Pro).IsAtLeast(PlanTier.Plus));
        Assert.False(Account(UserRole.User, PlanTier.Plus).IsAtLeast(PlanTier.Pro));
    }

    /// <summary>
    /// "paid" and "active" describe a subscription's status rather than which
    /// plan was bought, and once there are two paid plans they no longer say
    /// which. They resolve to the smaller one, because a value that arrives
    /// ambiguous must never be the reason someone gets more than they paid for.
    /// </summary>
    [Theory]
    [InlineData("pro", PlanTier.Pro)]
    [InlineData("PRO ", PlanTier.Pro)]
    [InlineData("plus", PlanTier.Plus)]
    [InlineData("paid", PlanTier.Plus)]
    [InlineData("active", PlanTier.Plus)]
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
