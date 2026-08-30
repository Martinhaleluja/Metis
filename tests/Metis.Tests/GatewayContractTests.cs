using System.Reflection;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// That the managed route carries everything a turn is made of.
///
/// The failure this guards against is silent, which is why it is worth a test
/// rather than a code review. Someone adds a field to GeminiRequest; every
/// provider running on the user's own key picks it up immediately, because they
/// are handed the object itself. The gateway route goes through a wire format,
/// and a field nobody remembered to add there simply never arrives — so managed
/// answers quietly get worse, with nothing in any log to say why.
/// </summary>
public sealed class GatewayContractTests
{
    /// <summary>
    /// The binary parts, which travel as their own multipart sections rather
    /// than as JSON properties, and the one field that is deliberately reshaped.
    /// </summary>
    private static readonly HashSet<string> CarriedSeparately =
    [
        nameof(GeminiRequest.ScreenshotBytes),
        nameof(GeminiRequest.RecordedAudioWav)
    ];

    [Fact]
    public void Every_part_of_a_turn_has_somewhere_to_travel()
    {
        var wire = typeof(AssistRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = typeof(GeminiRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => !CarriedSeparately.Contains(name))
            .Where(name => !wire.Contains(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "GeminiRequest has fields the managed route cannot carry, so a turn answered by Metis's own AI "
            + "would be missing context a turn on the user's own key has: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The one field where dropping it would be a privacy regression rather than
    /// a quality one.
    ///
    /// The prompt kernel uses it to tell the model that a black rectangle is
    /// something it was forbidden to see. Lose it in transit and the model
    /// describes a redacted banking window as a dark panel — confidently, and
    /// wrongly — which would be a new privacy failure created by the act of
    /// routing through a server.
    /// </summary>
    [Fact]
    public void Withheld_regions_survive_the_round_trip()
    {
        var original = new GeminiRequest(
            "What is this?",
            ScreenshotBytes: [1, 2, 3],
            AutomationContext: "Button 'Save'",
            Activation: ActivationKind.Inspect,
            Pointer: new PointerContext(100, 200, 500, 600, "Save"),
            Region: new ScreenRegion(10, 20, 30, 40, [new GuidancePoint(1, 1), new GuidancePoint(2, 2)]),
            WithheldScreenRegions: 3,
            UserName: "Ada",
            AcademicTeaching: true);

        var envelope = AssistRequest.FromGeminiRequest(original, "r1", null, "m", "chat", true);
        var rebuilt = envelope.ToGeminiRequest([1, 2, 3], null);

        Assert.Equal(3, rebuilt.WithheldScreenRegions);
        Assert.Equal("Button 'Save'", rebuilt.AutomationContext);
        Assert.Equal(ActivationKind.Inspect, rebuilt.Activation);
        Assert.Equal("Save", rebuilt.Pointer!.HoveredElement);
        Assert.Equal(30, rebuilt.Region!.NormalizedWidth);
        Assert.Equal(2, rebuilt.Region.Path.Count);
        Assert.True(rebuilt.AcademicTeaching);
        Assert.Equal("Ada", rebuilt.UserName);
    }

    /// <summary>
    /// The traced path travels as a count rather than as its points.
    ///
    /// The prompt only ever reports how many there were, and the path itself can
    /// run to hundreds of coordinates describing the exact shape of a gesture
    /// over someone's screen — which says more about what they were doing than
    /// the answer needs. Sending a number instead is the smaller thing to send.
    /// </summary>
    [Fact]
    public void A_traced_path_travels_as_a_count_not_as_coordinates()
    {
        var traced = new ScreenRegion(0, 0, 100, 100,
            Enumerable.Range(0, 250).Select(index => new GuidancePoint(index, index)).ToArray());

        var wire = AssistRegion.From(traced)!;

        Assert.Equal(250, wire.PathPointCount);
        Assert.Equal(250, wire.ToScreenRegion().Path.Count);
        Assert.True(wire.ToScreenRegion().IsUsable);
    }

    /// <summary>
    /// A malicious or broken client must not be able to make the gateway
    /// allocate an unbounded array by claiming a very large path.
    /// </summary>
    [Fact]
    public void An_absurd_path_count_is_clamped()
    {
        var wire = new AssistRegion(0, 0, 10, 10, PathPointCount: int.MaxValue);

        Assert.Equal(4096, wire.ToScreenRegion().Path.Count);
    }
}
