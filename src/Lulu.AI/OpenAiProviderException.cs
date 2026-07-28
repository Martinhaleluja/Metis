namespace Lulu.AI;

public enum OpenAiErrorKind
{
    Authentication,
    Permission,
    QuotaOrRateLimit,
    ModelUnavailable,
    InvalidRequest,
    Network,
    ServiceUnavailable,
    EmptyResponse
}

public sealed class OpenAiProviderException : Exception
{
    public OpenAiProviderException(
        OpenAiErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public OpenAiErrorKind Kind { get; }
    public int? StatusCode { get; }
}
