namespace Metis.Api;

/// <summary>
/// Checks that a customer's own API key actually works before it is stored.
///
/// Storing an unverified key means the customer connects a provider, sees a
/// success message, and then discovers at the worst possible moment that Metis
/// cannot use it — with nothing to distinguish "you pasted it wrong" from "Metis
/// is broken". One cheap call at connection time removes that whole class of
/// confusion.
///
/// Every endpoint below lists models. None of them run inference, so validating
/// costs the customer nothing.
///
/// This lives in Metis.Api rather than reusing the provider classes in Metis.AI
/// because the gateway deliberately does not reference Metis.AI: that assembly
/// targets a Windows framework in places and carries the whole assistant-plan
/// machinery, none of which belongs in a Linux container that only needs to make
/// one GET request.
/// </summary>
public sealed class ProviderKeyValidator(IHttpClientFactory clients, ILogger<ProviderKeyValidator> log)
{
    /// <summary>
    /// Whether the key works. Returns the reason on failure, in words a customer
    /// can act on — and never including the key or the provider's raw error,
    /// which can name the account the key belongs to.
    /// </summary>
    public async Task<(bool Ok, string? Reason)> ValidateAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var http = clients.CreateClient("providers");

        using var request = provider switch
        {
            "openai" => Bearer("https://api.openai.com/v1/models", apiKey),
            "openrouter" => Bearer("https://openrouter.ai/api/v1/key", apiKey),
            "mistral" => Bearer("https://api.mistral.ai/v1/models", apiKey),
            "anthropic" => Header("https://api.anthropic.com/v1/models", "x-api-key", apiKey, ("anthropic-version", "2023-06-01")),
            "google" => Header("https://generativelanguage.googleapis.com/v1beta/models", "x-goog-api-key", apiKey),
            _ => null
        } ?? throw new ArgumentOutOfRangeException(nameof(provider), $"No validator for '{provider}'.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return ((int)response.StatusCode switch
            {
                401 or 403 => false,
                429 => false,
                _ => false
            }, (int)response.StatusCode switch
            {
                401 or 403 => "That key was refused by the provider. Check you copied all of it, and that it is not revoked.",
                429 => "The provider is rate limiting this key right now. Try connecting again in a minute.",
                _ => "The provider could not confirm that key just now. Try again shortly."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "The provider did not answer in time. Try connecting again.");
        }
        catch (HttpRequestException exception)
        {
            // The exception message can contain the request URI. The key is
            // never in a URI here — every provider above takes it in a header
            // for exactly this reason — but the message still goes to the log
            // and not to the customer.
            log.LogWarning(exception, "Could not reach {Provider} to validate a key.", provider);
            return (false, "Metis could not reach that provider. Try again shortly.");
        }
    }

    private static HttpRequestMessage Bearer(string url, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static HttpRequestMessage Header(
        string url, string name, string apiKey, params (string Name, string Value)[] extra)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(name, apiKey);
        foreach (var (headerName, headerValue) in extra)
        {
            request.Headers.Add(headerName, headerValue);
        }

        return request;
    }

    /// <summary>
    /// The last few characters of a key, for showing the customer which one they
    /// connected without showing them the key.
    ///
    /// Capped at twelve characters because that is the column's check
    /// constraint, and four visible characters because that is enough to tell
    /// two keys apart and not enough to be worth stealing.
    /// </summary>
    public static string Hint(string apiKey)
    {
        var trimmed = apiKey?.Trim() ?? string.Empty;
        return trimmed.Length <= 4 ? "…" : "…" + trimmed[^4..];
    }
}
