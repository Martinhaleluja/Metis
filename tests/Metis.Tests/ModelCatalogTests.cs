using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// A picker that hides which models are billed is one that bills people by
/// accident, so the marking is the part worth pinning down.
/// </summary>
public sealed class ModelCatalogTests
{
    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenAI")]
    [InlineData("Claude")]
    [InlineData("OpenRouter")]
    [InlineData("Ollama")]
    public void Every_provider_offers_something(string provider) =>
        Assert.NotEmpty(ModelCatalog.For(provider));

    [Fact]
    public void An_unknown_provider_falls_back_to_gemini() =>
        Assert.Equal(ModelCatalog.Gemini, ModelCatalog.For("nonsense"));

    /// <summary>
    /// Metis reasons about a screenshot on every turn, so a model that cannot
    /// read one cannot do the job. Anything listed without vision has to say so
    /// rather than simply failing later.
    /// </summary>
    [Fact]
    public void Every_listed_model_either_sees_or_says_it_cannot() =>
        Assert.All(ModelCatalog.All, model =>
            Assert.True(model.SupportsVision || model.Summary.Contains("no screenshots")));

    [Fact]
    public void Local_models_are_marked_as_running_here() =>
        Assert.All(ModelCatalog.Ollama, model =>
        {
            Assert.Equal(ModelTier.Local, model.Tier);
            Assert.Contains("On this PC", model.Summary, StringComparison.Ordinal);
        });

    /// <summary>
    /// Google publishes a per-day figure for the 2.5 line but now sets it per
    /// account for the 3.x line and points at AI Studio instead. So the rule is
    /// not "every free model states an allowance" — it is that a stated
    /// allowance is a real one, and an unknown one is left off rather than
    /// invented. A guessed limit shown as fact is worse than no limit shown.
    /// </summary>
    [Fact]
    public void The_free_gemini_models_show_only_an_allowance_they_actually_have()
    {
        var free = ModelCatalog.Gemini.Where(model => model.Tier == ModelTier.Free).ToArray();

        Assert.NotEmpty(free);

        // Whether a model publishes a daily allowance is Google's choice, and
        // the current generation mostly does not. What matters is that Metis
        // never shows an allowance it cannot substantiate, which is what the
        // rest of this checks -- inventing a plausible number would be worse
        // than showing none.
        Assert.All(free, model =>
        {
            Assert.Contains("Free", model.Summary, StringComparison.Ordinal);

            if (model.RequestsPerDay > 0)
            {
                Assert.Contains($"{model.RequestsPerDay}/day", model.Summary, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("/day", model.Summary, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>
    /// Metis sends a screenshot on every turn, so a Gemini-key model that could
    /// not read one would be listed as usable and then fail on first use.
    /// </summary>
    [Fact]
    public void Every_gemini_key_model_can_read_a_screenshot() =>
        Assert.All(ModelCatalog.Gemini, model => Assert.True(model.SupportsVision));

    /// <summary>
    /// Gemma is served through the same Gemini key and is free of charge, so it
    /// belongs in that list rather than behind a separate provider.
    /// </summary>
    [Fact]
    public void Gemma_is_listed_under_the_gemini_key_and_marked_free()
    {
        var gemma = ModelCatalog.Gemini.Where(model => model.Id.StartsWith("gemma", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(gemma);
        Assert.All(gemma, model => Assert.Equal(ModelTier.Free, model.Tier));
    }

    /// <summary>
    /// The Pro line has no free allowance at all, so it must never be marked
    /// free: that is the marking that would bill someone by accident.
    /// </summary>
    [Fact]
    public void The_gemini_pro_models_are_marked_paid() =>
        Assert.All(
            ModelCatalog.Gemini.Where(model => model.Id.Contains("-pro", StringComparison.Ordinal)),
            model => Assert.Equal(ModelTier.Paid, model.Tier));

    [Fact]
    public void A_context_window_reads_in_the_units_people_use()
    {
        var big = new ModelOption("Gemini", "x", "X", ModelTier.Free, 1_000_000);
        var small = new ModelOption("Ollama", "y", "Y", ModelTier.Local, 32_000);

        Assert.Contains("1M context", big.Summary, StringComparison.Ordinal);
        Assert.Contains("32K context", small.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model the provider reports but the catalogue has never heard of is
    /// listed rather than hidden, and assumed paid — guessing that a stranger
    /// is free is the guess that costs the user money.
    /// </summary>
    [Fact]
    public void An_unknown_live_model_is_listed_and_assumed_paid()
    {
        var merged = ModelCatalog.Merge("Gemini", [("gemini-9-ultra", "Gemini 9 Ultra", 2_000_000)]);

        var added = Assert.Single(merged, model => model.Id == "gemini-9-ultra");
        Assert.Equal(ModelTier.Paid, added.Tier);
        Assert.Equal(2_000_000, added.ContextTokens);
    }

    [Fact]
    public void A_known_live_model_keeps_its_curated_marking_and_takes_the_live_window()
    {
        var merged = ModelCatalog.Merge("Gemini", [("gemini-3.5-flash", null, 2_000_000)]);

        var flash = Assert.Single(merged, model => model.Id == "gemini-3.5-flash");
        Assert.Equal(ModelTier.Free, flash.Tier);
        Assert.Equal(2_000_000, flash.ContextTokens);
    }

    /// <summary>
    /// A provider omitting a model from one response has not withdrawn it.
    /// </summary>
    [Fact]
    public void Curated_models_survive_a_thin_live_list() =>
        Assert.Equal(
            ModelCatalog.Gemini.Count,
            ModelCatalog.Merge("Gemini", [("gemini-3.5-flash", null, 0)]).Count);
}

/// <summary>
/// Usage is counted on this machine, because most requests go straight from the
/// desktop to the provider and never touch a Metis server.
/// </summary>
public sealed class ModelUsageLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly ModelOption Flash =
        new("Gemini", "gemini-2.5-flash", "Gemini 2.5 Flash", ModelTier.Free, 1_000_000, 10, 250);

    [Fact]
    public void Nothing_used_reads_as_nothing_used()
    {
        var usage = new ModelUsageLedger().For(Flash, Now);

        Assert.Equal(0, usage.Today);
        Assert.Equal(0, usage.ThisWeek);
        Assert.Equal(0d, usage.DailyFraction);
    }

    [Fact]
    public void Requests_land_in_the_hour_the_day_and_the_week()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record(Flash.Id, Now - TimeSpan.FromMinutes(10));
        ledger.Record(Flash.Id, Now - TimeSpan.FromHours(5));
        ledger.Record(Flash.Id, Now - TimeSpan.FromDays(3));

        var usage = ledger.For(Flash, Now);

        Assert.Equal(1, usage.LastHour);
        Assert.Equal(2, usage.Today);
        Assert.Equal(3, usage.ThisWeek);
    }

    /// <summary>
    /// A rolling window, not a calendar one. "In the last hour" is what a rate
    /// limit means, and a calendar hour would report nothing used a minute
    /// after the clock ticked over.
    /// </summary>
    [Fact]
    public void The_hour_rolls_rather_than_resetting_on_the_clock()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record(Flash.Id, Now - TimeSpan.FromMinutes(59));
        ledger.Record(Flash.Id, Now - TimeSpan.FromMinutes(61));

        Assert.Equal(1, ledger.For(Flash, Now).LastHour);
    }

    [Fact]
    public void Anything_older_than_a_week_stops_counting()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record(Flash.Id, Now - TimeSpan.FromDays(8));

        Assert.Equal(0, ledger.For(Flash, Now).ThisWeek);
    }

    [Fact]
    public void The_daily_share_is_capped_rather_than_overflowing()
    {
        var ledger = new ModelUsageLedger();
        foreach (var index in Enumerable.Range(0, 300))
        {
            ledger.Record(Flash.Id, Now - TimeSpan.FromMinutes(index));
        }

        Assert.Equal(1d, ledger.For(Flash, Now).DailyFraction);
    }

    /// <summary>
    /// Null rather than zero. An unknown limit drawn as an empty bar reads as
    /// "plenty left", which is the opposite of what is known.
    /// </summary>
    [Fact]
    public void An_unpublished_limit_has_no_fraction_at_all()
    {
        var paid = new ModelOption("OpenAI", "gpt-5", "GPT-5", ModelTier.Paid, 400_000);
        var ledger = new ModelUsageLedger();
        ledger.Record(paid.Id, Now);

        Assert.Null(ledger.For(paid, Now).DailyFraction);
    }

    [Fact]
    public void The_description_says_what_is_known_and_stays_quiet_about_the_rest()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record(Flash.Id, Now);

        var described = ledger.For(Flash, Now).Describe();

        Assert.Contains("1 of 250 today", described, StringComparison.Ordinal);
        Assert.Contains("this week", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_ledger_comes_back_without_the_stale_entries()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record(Flash.Id, Now - TimeSpan.FromHours(2));
        ledger.Record(Flash.Id, Now - TimeSpan.FromDays(9));

        var restored = new ModelUsageLedger();
        restored.Restore(ledger.Snapshot(), Now);

        Assert.Equal(1, restored.For(Flash, Now).ThisWeek);
    }

    [Fact]
    public void The_models_actually_used_are_reported_busiest_first()
    {
        var ledger = new ModelUsageLedger();
        ledger.Record("quiet", Now);
        ledger.Record("busy", Now);
        ledger.Record("busy", Now);

        Assert.Equal(["busy", "quiet"], ledger.UsedModels(Now));
    }
}
