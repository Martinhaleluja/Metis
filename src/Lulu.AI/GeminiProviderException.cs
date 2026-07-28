namespace Lulu.AI;

public enum GeminiErrorKind
{
    Authentication,
    Permission,
    ModelUnavailable,
    QuotaOrRateLimit,
    InvalidRequest,
    Network,
    ServiceUnavailable,
    EmptyResponse,
    Unknown
}

public sealed class GeminiProviderException : Exception
{
    public GeminiProviderException(
        GeminiErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public GeminiErrorKind Kind { get; }
    public int? StatusCode { get; }
}

