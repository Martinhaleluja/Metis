using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The plans, as a customer is told about them.
///
/// These are the numbers on the pricing page, the plan switcher in the notch,
/// and the sentence in every upgrade prompt. They are also written down a
/// second time in Postgres (plan_limits) and a third in the website's
/// plans.ts, because each of those has to work without the other two. That is
/// three copies of a price, which is exactly the arrangement that eventually
/// disagrees with itself — so the shape of the ladder is asserted here, where a
/// disagreement is a red test rather than a customer seeing one number and
/// being charged another.
/// </summary>
public sealed class PlanCatalogueTests
{
    [Fact]
    public void There_are_three_plans_cheapest_first()
    {
        Assert.Equal(
            [PlanTier.Free, PlanTier.Pro, PlanTier.Max],
            PlanCatalogue.All.Select(offer => offer.Tier));

        Assert.Equal(
            PlanCatalogue.All.Select(offer => offer.PriceUsd).OrderBy(price => price),
            PlanCatalogue.All.Select(offer => offer.PriceUsd));
    }

    [Fact]
    public void The_prices_are_the_ones_that_were_agreed()
    {
        Assert.Equal(0, PlanCatalogue.Free.PriceUsd);
        Assert.Equal(20, PlanCatalogue.Pro.PriceUsd);
        Assert.Equal(50, PlanCatalogue.Max.PriceUsd);
    }

    /// <summary>
    /// Free's three numbers, exactly as the pricing page states them: fifty
    /// talk messages, plenty of dictation, ten agent messages.
    /// </summary>
    [Fact]
    public void Free_gets_what_the_page_promises()
    {
        var free = PlanCatalogue.Free.Limits;

        Assert.Equal(50, free.MaxTurnsPerMonth);
        Assert.Equal(10, free.MaxAgentStepsPerMonth);

        // "Plenty" is not a number, but it is not nothing either: enough to
        // dictate for several hours a month, and definitely capped.
        Assert.InRange(free.MaxDictationMinutesPerMonth, 120, 1000);
    }

    /// <summary>
    /// The paid plans stop counting answers and dictation. A count on a plan
    /// somebody is paying for refuses a person who has spent almost nothing,
    /// which is why the ceiling there is money instead.
    /// </summary>
    [Theory]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Max)]
    public void Paid_plans_do_not_count_talking_or_dictating(PlanTier tier)
    {
        var limits = PlanCatalogue.LimitsFor(tier);

        Assert.Equal(0, limits.MaxTurnsPerMonth);
        Assert.Equal(0, limits.MaxDictationMinutesPerMonth);
        Assert.True(limits.MonthlyBudgetUsd > 0);
    }

    [Fact]
    public void Pro_gets_four_hundred_agent_messages() =>
        Assert.Equal(400, PlanCatalogue.Pro.Limits.MaxAgentStepsPerMonth);

    /// <summary>
    /// Every allowance goes up, or stays uncapped, as the price does. A higher
    /// plan that quietly gave less of something would be the kind of mistake
    /// nobody notices until a customer does.
    /// </summary>
    [Fact]
    public void Nothing_gets_smaller_as_the_price_goes_up()
    {
        for (var i = 1; i < PlanCatalogue.All.Count; i++)
        {
            var below = PlanCatalogue.All[i - 1].Limits;
            var above = PlanCatalogue.All[i].Limits;
            var name = PlanCatalogue.All[i].Name;

            Assert.True(above.MonthlyBudgetUsd >= below.MonthlyBudgetUsd, $"{name}: budget");
            Assert.True(above.MaxScreenshotBytes >= below.MaxScreenshotBytes, $"{name}: screenshot");
            Assert.True(above.MemoryEntriesMax >= below.MemoryEntriesMax, $"{name}: memory");
            Assert.True(above.MaxAgentStepsPerMonth >= below.MaxAgentStepsPerMonth, $"{name}: agents");
            Assert.True(above.RequestsPerMinute >= below.RequestsPerMinute, $"{name}: rate");

            Assert.True(Uncapped(above.MaxTurnsPerMonth, below.MaxTurnsPerMonth), $"{name}: talk");
            Assert.True(
                Uncapped(above.MaxDictationMinutesPerMonth, below.MaxDictationMinutesPerMonth),
                $"{name}: dictation");
        }

        // Zero means "no cap", so it is the largest value rather than the
        // smallest — comparing these two numerically is precisely the mistake
        // this helper exists to stop.
        static bool Uncapped(int above, int below) => above == 0 || (below != 0 && above >= below);
    }

    /// <summary>
    /// Bringing your own AI account is Max's, and it is the thing that
    /// justifies the step from $20 to $50. If it ever became available lower
    /// down, the top plan would have nothing left to sell.
    /// </summary>
    [Fact]
    public void Only_Max_may_bring_its_own_key()
    {
        static MetisAccount On(PlanTier tier) =>
            new("u_1", UserRole.User, tier, MetisEnvironment.Production);

        Assert.True(Entitlements.Has(On(PlanTier.Max), MetisFeature.CustomAiProvider, true));
        Assert.False(Entitlements.Has(On(PlanTier.Pro), MetisFeature.CustomAiProvider, true));
        Assert.False(Entitlements.Has(On(PlanTier.Free), MetisFeature.CustomAiProvider, true));

        Assert.Contains("own AI account", PlanCatalogue.Max.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The upgrade prompt has to know where the ladder ends. It used to say
    /// "upgrade to Plus" unconditionally, which offered the middle plan to
    /// somebody already on it.
    /// </summary>
    [Fact]
    public void The_next_plan_up_is_known_and_the_top_has_none()
    {
        Assert.Equal(PlanTier.Pro, PlanCatalogue.NextAfter(PlanTier.Free)?.Tier);
        Assert.Equal(PlanTier.Max, PlanCatalogue.NextAfter(PlanTier.Pro)?.Tier);
        Assert.Null(PlanCatalogue.NextAfter(PlanTier.Max));
    }

    /// <summary>
    /// Every plan says what it is in a sentence, and the sentence names the
    /// units it is metered in rather than adjectives. "Powerful AI" is not
    /// something a person can check they got.
    /// </summary>
    [Fact]
    public void Every_plan_describes_itself_in_countable_terms()
    {
        foreach (var offer in PlanCatalogue.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(offer.Name));
            Assert.False(string.IsNullOrWhiteSpace(offer.Summary));
            Assert.EndsWith(".", offer.Summary);
            Assert.Contains("agent messages", offer.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_price_label_reads_as_money_or_as_free()
    {
        Assert.Equal("Free", PlanCatalogue.Free.PriceLabel);
        Assert.Equal("$20", PlanCatalogue.Pro.PriceLabel);
        Assert.Equal("$50", PlanCatalogue.Max.PriceLabel);
    }

    /// <summary>
    /// A tier maps to its own offer, and an unknown one falls to Free rather
    /// than throwing. Guessing wrong towards less access is the only failure
    /// here that costs nothing.
    /// </summary>
    [Fact]
    public void Every_tier_resolves_to_itself()
    {
        foreach (var tier in Enum.GetValues<PlanTier>())
        {
            Assert.Equal(tier, PlanCatalogue.For(tier).Tier);
        }
    }
}
