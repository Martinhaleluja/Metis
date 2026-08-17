using System.Windows.Media;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The companion's form is a preference, and a preference must never be able to
/// break the companion. These check that every form is drawable, that a stored
/// name Metis no longer recognises still leaves a visible companion, and that
/// choosing a form cannot change what Metis is allowed to do.
/// </summary>
public sealed class CompanionShapeTests
{
    [Fact]
    public void Every_shape_parses_into_real_geometry()
    {
        foreach (var shape in CompanionShapes.All)
        {
            var geometry = Geometry.Parse(shape.Geometry);
            var bounds = geometry.Bounds;

            Assert.False(bounds.IsEmpty);
            Assert.True(bounds.Width > 8, $"{shape.Name} is too narrow to see at companion size.");
            Assert.True(bounds.Height > 8, $"{shape.Name} is too short to see at companion size.");
        }
    }

    [Fact]
    public void Every_shape_fits_the_design_box()
    {
        // Authored in a 72x72 box. A silhouette that spills outside it would be
        // clipped or shrunk relative to the others once laid out.
        foreach (var shape in CompanionShapes.All)
        {
            var bounds = Geometry.Parse(shape.Geometry).Bounds;

            Assert.True(bounds.Left >= -2, $"{shape.Name} starts left of the box.");
            Assert.True(bounds.Top >= -2, $"{shape.Name} starts above the box.");
            Assert.True(bounds.Right <= 74, $"{shape.Name} runs past the right of the box.");
            Assert.True(bounds.Bottom <= 74, $"{shape.Name} runs past the bottom of the box.");
        }
    }

    [Fact]
    public void Indicators_stay_inside_the_silhouette()
    {
        // The speaking bars and thinking ring are drawn at the indicator point.
        // If that point is not on the shape, the companion appears to be
        // talking out of thin air beside itself.
        foreach (var shape in CompanionShapes.All)
        {
            var geometry = Geometry.Parse(shape.Geometry);
            var bounds = geometry.Bounds;

            // The indicator offset is in rendered units from the centre of the
            // box; the shape is fitted uniformly into that box first.
            var fit = 60 / Math.Max(bounds.Width, bounds.Height);
            var point = new System.Windows.Point(
                bounds.X + (bounds.Width / 2) + (shape.IndicatorX / fit),
                bounds.Y + (bounds.Height / 2) + (shape.IndicatorY / fit));

            Assert.True(
                geometry.FillContains(point),
                $"{shape.Name}'s indicators land off the shape at {point}.");
        }
    }

    [Fact]
    public void An_unknown_shape_falls_back_rather_than_vanishing()
    {
        var resolved = CompanionShapes.Resolve("Dodecahedron");

        Assert.Equal(CompanionShapes.DefaultName, resolved.Name);
        Assert.False(string.IsNullOrWhiteSpace(resolved.Geometry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_names_resolve_to_the_default(string? name) =>
        Assert.Equal(CompanionShapes.DefaultName, CompanionShapes.Normalize(name));

    [Fact]
    public void Names_resolve_regardless_of_casing() =>
        Assert.Equal("Cursor", CompanionShapes.Normalize("cUrSoR"));

    [Fact]
    public void Settings_normalise_an_unrecognised_shape()
    {
        var settings = new AppSettings { CompanionShape = "not-a-shape" }.Normalize();

        Assert.Equal(CompanionShapes.DefaultName, settings.CompanionShape);
    }

    [Fact]
    public void Settings_keep_a_shape_the_user_chose()
    {
        var settings = new AppSettings { CompanionShape = "Cursor" }.Normalize();

        Assert.Equal("Cursor", settings.CompanionShape);
    }

    [Fact]
    public void Shape_names_are_distinct()
    {
        var names = CompanionShapes.All.Select(shape => shape.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_default_shape_exists_in_the_catalogue() =>
        Assert.Contains(CompanionShapes.All, shape => shape.Name == CompanionShapes.DefaultName);

    [Fact]
    public void Every_shape_describes_itself()
    {
        // The picker shows these, and an unexplained silhouette is a guess.
        foreach (var shape in CompanionShapes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(shape.Description), $"{shape.Name} has no description.");
        }
    }
}
