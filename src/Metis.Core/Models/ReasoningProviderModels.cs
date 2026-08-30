namespace Metis.Core.Models;

[Flags]
public enum ReasoningProviderCapabilities
{
    None = 0,
    Text = 1 << 0,
    Vision = 1 << 1,
    ModelDiscovery = 1 << 2,
    StructuredPlans = 1 << 3,
    LocalEndpoint = 1 << 4,
    RemoteEndpoint = 1 << 5,
    AgentGateway = 1 << 6
}

public enum ReasoningAuthenticationKind
{
    None,
    ApiKey,
    OptionalBearerToken
}

public sealed record ReasoningProviderDescriptor(
    string Id,
    string DisplayName,
    ReasoningAuthenticationKind Authentication,
    ReasoningProviderCapabilities Capabilities);

public sealed record ReasoningModelInfo(
    string Name,
    string DisplayName,
    ReasoningProviderCapabilities Capabilities);

public sealed record ReasoningResponse(
    string Text,
    string Model,
    string ProviderId,
    AssistantPlan Plan,

    /// <summary>
    /// What the turn cost, when the provider says.
    ///
    /// Added with a default so the four existing implementations did not have to
    /// change: only the managed route knows this, because only there does Metis
    /// see the token counts rather than the user's own provider account. It
    /// feeds the same cost display that GeminiProvider.LastUsage already does,
    /// so a managed turn is not silently missing from it.
    /// </summary>
    ModelUsageReport? Usage = null);
