namespace Metis.Core.Models;

/// <summary>
/// Why a reasoning request failed, in terms the user interface can act on
/// without knowing which provider was asked.
///
/// It lives in Metis.Core rather than beside the provider implementations
/// because the gateway raises the same failures on the server side and the
/// desktop has to recognise them when they come back over the wire. Two copies
/// of this taxonomy would mean a refusal that means one thing on one side and
/// something else on the other.
/// </summary>
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

    /// <summary>
    /// The request was well-formed and the credential was good, but the
    /// account's plan does not include what was asked for.
    ///
    /// This is deliberately not <see cref="Permission"/>. Permission tells the
    /// user their key is wrong, and a user whose key is fine will go and
    /// regenerate a working key looking for a fault that is not there. The plan
    /// is small; the key is fine; those are different sentences.
    /// </summary>
    PlanLimited,

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
