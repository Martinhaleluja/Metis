using System.Globalization;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class CompanionPaletteTests
{
    [Fact]
    public void The_palette_offers_ten_choices() => Assert.Equal(10, CompanionPalette.All.Count);

    [Fact]
    public void Every_option_has_a_distinct_name_and_fill()
    {
        Assert.Equal(
            CompanionPalette.All.Count,
            CompanionPalette.All.Select(option => option.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            CompanionPalette.All.Count,
            CompanionPalette.All.Select(option => option.Fill).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Ocean")]
    [InlineData("ocean")]
    [InlineData("  OCEAN  ")]
    public void A_name_resolves_regardless_of_case_or_padding(string name) =>
        Assert.Equal("Ocean", CompanionPalette.Resolve(name).Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Chartreuse")]
    public void An_unknown_colour_falls_back_rather_than_throwing(string? name) =>
        Assert.Equal(CompanionPalette.DefaultName, CompanionPalette.Resolve(name).Name);

    [Fact]
    public void Every_colour_parses_as_a_hex_value()
    {
        foreach (var option in CompanionPalette.All)
        {
            foreach (var hex in new[] { option.Fill, option.Glow })
            {
                Assert.StartsWith("#", hex, StringComparison.Ordinal);
                Assert.Equal(7, hex.Length);
                Assert.True(
                    int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
                    $"{option.Name} has an unparseable colour '{hex}'");
            }
        }
    }

    [Fact]
    public void Settings_normalise_an_unknown_colour_to_the_default()
    {
        var normalized = (new AppSettings { CompanionColor = "not-a-colour" }).Normalize();

        Assert.Equal(CompanionPalette.DefaultName, normalized.CompanionColor);
    }

    [Fact]
    public void Settings_keep_a_valid_colour_choice()
    {
        var normalized = (new AppSettings { CompanionColor = "violet" }).Normalize();

        Assert.Equal("Violet", normalized.CompanionColor);
    }
}
