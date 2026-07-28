namespace Lulu.AI;

public enum ExternalVoiceErrorKind
{
    Authentication,
    Permission,
    QuotaOrRateLimit,
    InvalidRequest,
    ServiceUnavailable,
    Network,
    EmptyResponse,
    Unknown
}

public sealed class ExternalVoiceProviderException : Exception
{
    public ExternalVoiceProviderException(
        string provider,
        ExternalVoiceErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        Kind = kind;
        StatusCode = statusCode;
    }

    public string Provider { get; }
    public ExternalVoiceErrorKind Kind { get; }
    public int? StatusCode { get; }
}

