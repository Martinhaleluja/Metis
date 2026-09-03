namespace Metis.ApiTests;

/// <summary>
/// A placeholder that exists so the project is real from the moment it is added
/// to the solution. An empty test project is reported as a build success and a
/// test run of nothing, which looks identical to a project that was never
/// wired up. The real gateway tests replace this as each subsystem lands.
/// </summary>
public sealed class GatewayHealthTests
{
    [Fact]
    public void TheApiProjectIsReferenceableFromTests()
    {
        // Program is generated as an internal partial class for a top-level
        // statements program; the assembly is what we are proving is reachable.
        var assembly = typeof(Metis.Api.GatewayConfig).Assembly;
        Assert.Equal("Metis.Api", assembly.GetName().Name);
    }
}
