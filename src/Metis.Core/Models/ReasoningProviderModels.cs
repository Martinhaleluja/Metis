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
    AssistantPlan Plan);
