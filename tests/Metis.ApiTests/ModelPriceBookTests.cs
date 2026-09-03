using Metis.Api;

namespace Metis.ApiTests;

/// <summary>
/// What a turn cost, and what happens when nobody has priced the model.
///
/// The fallback is the interesting part. Costing an unknown model at zero is
/// tidy and unbounded: it would be invisible to the monthly budget and could be
/// used without limit. Guessing high is a slightly overstated bill that shows up
/// in reporting. One of those is recoverable.
/// </summary>
public sealed class ModelPriceBookTests
{
    private static ModelPriceBook Book() => new(
    [
        new ModelPrice("google", "gemini-2.5-flash-lite", 0.10m, 0.40m, DateTimeOffset.UtcNow.AddDays(-30)),
        new ModelPrice("google", "gemini-2.5-flash", 0.30m, 2.50m, DateTimeOffset.UtcNow.AddDays(-30)),
        new ModelPrice("google", "gemini-2.5-pro", 2.50m, 15.00m, DateTimeOffset.UtcNow.AddDays(-30))
    ]);

    [Fact]
    public void A_priced_model_is_costed_from_its_own_row()
    {
        var (cost, estimated) = Book().Estimate("google", "gemini-2.5-flash", 1_000_000, 1_000_000);

        Assert.Equal(2.80m, cost);
        Assert.False(estimated);
    }

    [Fact]
    public void Small_turns_are_costed_to_six_decimal_places()
    {
        var (cost, _) = Book().Estimate("google", "gemini-2.5-flash-lite", 1_200, 300);

        // 1200/1e6 * 0.10 + 300/1e6 * 0.40
        Assert.Equal(0.00024m, cost);
    }

    /// <summary>
    /// An unpriced model is charged at the dearest row that provider has, and
    /// flagged, so it appears in reporting as an estimate rather than vanishing.
    /// </summary>
    [Fact]
    public void An_unknown_model_is_priced_at_the_provider_s_dearest()
    {
        var (cost, estimated) = Book().Estimate("google", "gemini-9-experimental", 1_000_000, 1_000_000);

        Assert.Equal(17.50m, cost);
        Assert.True(estimated);
    }

    [Fact]
    public void An_unknown_provider_is_reported_as_an_estimate()
    {
        var (_, estimated) = Book().Estimate("nobody", "anything", 1_000, 1_000);

        Assert.True(estimated);
    }

    /// <summary>
    /// A price change is an insert rather than an update, so a usage row costed
    /// last month stays explicable. The newest row for a model is the live one.
    /// </summary>
    [Fact]
    public void The_newest_row_for_a_model_wins()
    {
        var book = new ModelPriceBook(
        [
            new ModelPrice("google", "gemini-2.5-flash", 0.30m, 2.50m, DateTimeOffset.UtcNow.AddDays(-90)),
            new ModelPrice("google", "gemini-2.5-flash", 0.20m, 1.50m, DateTimeOffset.UtcNow.AddDays(-1))
        ]);

        var (cost, _) = book.Estimate("google", "gemini-2.5-flash", 1_000_000, 0);

        Assert.Equal(0.20m, cost);
    }

    [Fact]
    public void An_empty_book_costs_nothing_and_says_it_is_guessing()
    {
        var (cost, estimated) = ModelPriceBook.Empty.Estimate("google", "gemini-2.5-flash", 500, 500);

        Assert.Equal(0m, cost);
        Assert.True(estimated);
    }
}

/// <summary>
/// When this month's allowance resets. Trivial arithmetic that is easy to get
/// wrong at a year boundary, and getting it wrong shows a paying customer the
/// wrong date on their own account page.
/// </summary>
public sealed class UsageSnapshotTests
{
    [Fact]
    public void The_allowance_resets_at_the_start_of_next_month()
    {
        var snapshot = new UsageSnapshot(1m, 5, 0, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), snapshot.ResetsUtc);
    }

    [Fact]
    public void December_rolls_into_the_next_year()
    {
        var snapshot = new UsageSnapshot(1m, 5, 0, new DateTimeOffset(2026, 12, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.ResetsUtc);
    }
}
