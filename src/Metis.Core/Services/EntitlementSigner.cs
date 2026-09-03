using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Signs and verifies the entitlement snapshot the gateway hands the client.
///
/// The problem it solves is narrow and worth stating precisely. Metis works
/// offline, and a paying user who opens their laptop on a train must not be
/// silently treated as if they were on the free plan for a week. So the client
/// caches what the server last told it. But a cache the client writes is a cache
/// the client can edit, and "edit one file to become Pro" is not an acceptable
/// upgrade path.
///
/// A signature fixes the second problem without giving up the first: the client
/// can read the cached snapshot, and cannot write a new one the gateway would
/// not have written.
///
/// Be honest about the limit. Whoever runs the program owns the machine and can
/// patch the binary; nothing here stops that and nothing here tries. What this
/// buys is that the *ordinary* path is honest, and the gateway checks every
/// request again anyway. A forged offline Pro unlocks local behaviour running on
/// the user's own API key, which costs Metis nothing — which is exactly why this
/// does not need key rotation, revocation lists, or any of the machinery a real
/// licence system would.
///
/// ECDSA P-256 over SHA-256 because it is in the box on net8.0, on Windows and
/// in a Linux container alike. Ed25519 would be the nicer primitive; .NET 8 has
/// no in-box implementation, and pulling a crypto library into a WPF application
/// for one signature check is a worse trade than a slightly older curve.
/// </summary>
public static class EntitlementSigner
{
    /// <summary>
    /// How long a snapshot stays usable offline. Deliberately the same as
    /// <see cref="StartupAuthGate.OfflineGrace"/>: two different windows for
    /// "how long may this keep working without the server" would eventually
    /// disagree, and the day they did, someone would be signed out of an
    /// application that still believed they were on Pro.
    /// </summary>
    public static readonly TimeSpan Lifetime = StartupAuthGate.OfflineGrace;

    private static readonly JsonSerializerOptions Canonical = new()
    {
        // No indentation, no reordering, properties written in declaration
        // order. The bytes have to be reproducible on both sides or the
        // signature is over something the verifier never sees.
        WriteIndented = false
    };

    /// <summary>
    /// The exact bytes that get signed. Written by hand rather than by
    /// serialising the response record, because the response record is a wire
    /// format that will gain fields, and a signature over a growing object is a
    /// signature that breaks every time someone adds one.
    /// </summary>
    public static byte[] CanonicalBytes(EntitlementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var payload = new
        {
            v = 1,
            userId = snapshot.UserId,
            role = snapshot.Role.ToString().ToLowerInvariant(),
            plan = snapshot.Plan.ToString().ToLowerInvariant(),
            emailVerified = snapshot.EmailVerified,
            billingIsLive = snapshot.BillingIsLive,
            features = snapshot.Granted
                .Select(feature => feature.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            limits = new
            {
                snapshot.Limits.MonthlyBudgetUsd,
                snapshot.Limits.MaxScreenshotBytes,
                snapshot.Limits.RequestsPerMinute,
                snapshot.Limits.BurstRequests,
                snapshot.Limits.MaxAgentStepsPerMonth,
                snapshot.Limits.MaxAgentStepsPerTask,
                snapshot.Limits.MemoryEntriesMax,
                snapshot.Limits.MaxTurnsPerMonth,

                // Signed like every other allowance, which it was not until now.
                // Both halves of this file worked from the same field list, so
                // the signature still verified and nothing ever failed — the
                // number was simply absent from the payload, and every snapshot
                // read back offline reported a dictation cap of zero. Zero means
                // "no cap" in PlanLimits, and the meter draws no cap as
                // Unlimited, so a Free account that went offline was shown
                // unlimited dictation it does not have.
                snapshot.Limits.MaxDictationMinutesPerMonth,
                managedModels = snapshot.Limits.ManagedModels
                    .OrderBy(model => model, StringComparer.Ordinal).ToArray()
            },
            issuedUtc = snapshot.IssuedUtc.ToUniversalTime().ToString("O"),
            expiresUtc = snapshot.ExpiresUtc.ToUniversalTime().ToString("O")
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload, Canonical);
    }

    /// <summary>
    /// Produces <c>base64url(payload).base64url(signature)</c>. The payload
    /// travels alongside the signature rather than being reconstructed by the
    /// verifier, so a field added on the server does not make every older client
    /// reject a snapshot it simply does not understand yet.
    /// </summary>
    public static string Sign(EntitlementSnapshot snapshot, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        var payload = CanonicalBytes(snapshot);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    /// <summary>
    /// Reads a signed snapshot back, or returns null.
    ///
    /// Every failure — bad shape, bad signature, expired, issued for somebody
    /// else — returns null rather than throwing or reporting which check failed.
    /// The caller's only correct response to any of them is the same: fall back
    /// to the free plan and ask the server again when it can.
    /// </summary>
    public static EntitlementSnapshot? Verify(
        string? signed,
        ECDsa publicKey,
        string expectedUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (string.IsNullOrWhiteSpace(signed))
        {
            return null;
        }

        var separator = signed.IndexOf('.');
        if (separator <= 0 || separator == signed.Length - 1)
        {
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = FromBase64Url(signed[..separator]);
            signature = FromBase64Url(signed[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var snapshot = new EntitlementSnapshot(
                root.GetProperty("userId").GetString() ?? string.Empty,
                Entitlements.ParseRole(root.GetProperty("role").GetString()),
                Entitlements.ParsePlan(root.GetProperty("plan").GetString()),
                root.GetProperty("emailVerified").GetBoolean(),
                root.GetProperty("billingIsLive").GetBoolean(),
                ReadFeatures(root),
                ReadLimits(root.GetProperty("limits")),
                root.GetProperty("issuedUtc").GetDateTimeOffset(),
                root.GetProperty("expiresUtc").GetDateTimeOffset());

            return snapshot.IsUsableAt(now, expectedUserId) ? snapshot : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static IReadOnlySet<MetisFeature> ReadFeatures(JsonElement root)
    {
        var granted = new HashSet<MetisFeature>();
        foreach (var element in root.GetProperty("features").EnumerateArray())
        {
            // A feature name this build does not know about is a newer server
            // talking to an older client. Skipping it is the safe direction:
            // the client simply will not offer something it cannot render.
            if (Enum.TryParse<MetisFeature>(element.GetString(), ignoreCase: false, out var feature))
            {
                granted.Add(feature);
            }
        }

        return granted;
    }

    /// <summary>
    /// Every allowance, read back in the order <see cref="PlanLimits"/> declares
    /// them. All eleven of them: this reconstructed ten for a while, and the one
    /// it dropped came back as zero, which the interface reads as "no limit".
    ///
    /// The last two are read with <c>TryGetProperty</c> because a snapshot signed
    /// by an older gateway will not carry them, and refusing to read such a
    /// snapshot at all would sign a paying user out of their own plan the day
    /// this shipped. The payload travels with the signature, so an old one still
    /// verifies; it simply says less.
    /// </summary>
    private static PlanLimits ReadLimits(JsonElement limits) => new(
        limits.GetProperty("MonthlyBudgetUsd").GetDecimal(),
        limits.GetProperty("MaxScreenshotBytes").GetInt32(),
        limits.GetProperty("RequestsPerMinute").GetInt32(),
        limits.GetProperty("BurstRequests").GetInt32(),
        limits.GetProperty("MaxAgentStepsPerMonth").GetInt32(),
        limits.GetProperty("MaxAgentStepsPerTask").GetInt32(),
        limits.GetProperty("MemoryEntriesMax").GetInt32(),
        limits.GetProperty("managedModels").EnumerateArray()
            .Select(model => model.GetString() ?? string.Empty)
            .Where(model => model.Length > 0)
            .ToArray(),
        limits.TryGetProperty("MaxTurnsPerMonth", out var turns) ? turns.GetInt32() : 0,
        limits.TryGetProperty("MaxDictationMinutesPerMonth", out var dictation) ? dictation.GetInt32() : 0);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException("Not base64url.") };
        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Loads a public key from the compiled-in base64 SubjectPublicKeyInfo, or
    /// null when the build carries no key. A build with no key simply never
    /// trusts a cached snapshot, which is the correct behaviour rather than an
    /// error: it falls back to asking the server every time.
    /// </summary>
    public static ECDsa? TryLoadPublicKey(string? base64SubjectPublicKeyInfo)
    {
        if (string.IsNullOrWhiteSpace(base64SubjectPublicKeyInfo))
        {
            return null;
        }

        try
        {
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64SubjectPublicKeyInfo.Trim()), out _);
            return key;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the signing key from the base64 PKCS#8 private key the gateway
    /// holds in an environment variable.
    /// </summary>
    public static ECDsa? TryLoadPrivateKey(string? base64Pkcs8)
    {
        if (string.IsNullOrWhiteSpace(base64Pkcs8))
        {
            return null;
        }

        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(base64Pkcs8.Trim()), out _);
            return key;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a fresh key pair, for standing the gateway up the first time.
    /// Returns base64 PKCS#8 private and base64 SubjectPublicKeyInfo public.
    /// </summary>
    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

}
