namespace Lulu.AI;

public enum ReasoningProviderErrorKind
{
    Authentication,
    Permission,
    QuotaOrRateLimit,
    InvalidRequest,
    ModelUnavailable,
    ServiceUnavailable,
    Network,
    EmptyResponse,
    InvalidEndpoint,
    Unknown
}

/// <summary>
/// A provider-neutral failure that the setup and assistant surfaces can show directly.
/// Messages are intentionally free of request URLs and credentials.
/// </summary>
public sealed class ReasoningProviderException : Exception
{
    public ReasoningProviderException(
        string providerId,
        ReasoningProviderErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderId = providerId;
        Kind = kind;
        StatusCode = statusCode;
    }

    public string ProviderId { get; }
    public ReasoningProviderErrorKind Kind { get; }
    public int? StatusCode { get; }
}
