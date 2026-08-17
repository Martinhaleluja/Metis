using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Every permission decision runs through one function, so these are the cases
/// that decide who gets what. A check written in two places will eventually be
/// written differently, and the copy that drifts is the one that grants access
/// it should not.
/// </summary>
public sealed class EntitlementTests
{
    private static MetisAccount Account(UserRole role, PlanTier plan = PlanTier.Free) =>
        new("u_1", role, plan, MetisEnvironment.Production);

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
    public void Paid_capabilities_are_free_for_now(MetisFeature feature)
    {
        Assert.False(Entitlements.BillingIsLive);
        Assert.True(Entitlements.Has(Account(UserRole.User), feature));
        Assert.True(Entitlements.Has(MetisAccount.SignedOut, feature));
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
        Assert.False(Entitlements.Has(Account(UserRole.User, PlanTier.Pro), feature));
        Assert.False(Entitlements.Has(MetisAccount.SignedOut, feature));
    }

    [Fact]
    public void A_free_capability_says_why_it_is_free() =>
        Assert.Contains("early access",
            Entitlements.Explain(Account(UserRole.User), MetisFeature.CustomAiProvider),
            StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Pro)]
    public void Developer_mode_is_closed_to_ordinary_accounts(UserRole role) =>
        Assert.False(Entitlements.Has(Account(role, PlanTier.Pro), MetisFeature.DeveloperMode));

    /// <summary>
    /// The narrowest staff capability. Being able to test the product is not
    /// the same as being able to read the company's numbers.
    /// </summary>
    [Fact]
    public void The_dashboard_is_narrower_than_the_rest_of_staff_access()
    {
        Assert.True(Entitlements.Has(Account(UserRole.Founder), MetisFeature.AdminDashboard));
        Assert.True(Entitlements.Has(Account(UserRole.Admin), MetisFeature.AdminDashboard));
        Assert.False(Entitlements.Has(Account(UserRole.Developer), MetisFeature.AdminDashboard));
    }

    /// <summary>
    /// An unverified address is an unproven claim to it, and that still gates
    /// the staff capabilities while everything else is free.
    /// </summary>
    [Fact]
    public void An_unverified_account_earns_no_staff_access()
    {
        var unverified = Account(UserRole.Founder, PlanTier.Pro) with { EmailVerified = false };

        Assert.False(Entitlements.Has(unverified, MetisFeature.DeveloperMode));
        Assert.False(Entitlements.Has(unverified, MetisFeature.AdminDashboard));
    }

    [Fact]
    public void A_refusal_says_something_the_user_can_act_on() =>
        Assert.Contains("Sign in",
            Entitlements.Explain(MetisAccount.SignedOut, MetisFeature.DeveloperMode),
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

    [Theory]
    [InlineData("free")]
    [InlineData(null)]
    [InlineData("cancelled")]
    public void Anything_but_an_active_plan_is_free(string? value) =>
        Assert.Equal(PlanTier.Free, Entitlements.ParsePlan(value));
}
