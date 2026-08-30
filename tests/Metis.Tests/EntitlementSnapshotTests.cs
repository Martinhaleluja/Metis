using System.Security.Cryptography;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The signed plan the client caches so an offline subscriber is not silently
/// treated as free.
///
/// Every failure has to land on the same side: unusable, therefore free. A
/// snapshot that is expired, tampered with, or issued for somebody else must
/// never be the reason someone appears to be on Pro — not because it would cost
/// Metis anything (the gateway re-checks every request), but because a client
/// that shows a plan the server will not honour is a client that lies to its
/// user about what they can do.
/// </summary>
public sealed class EntitlementSnapshotTests
{
    private static EntitlementSnapshot Snapshot(
        string userId = "u_1",
        PlanTier plan = PlanTier.Pro,
        DateTimeOffset? issued = null)
    {
        var at = issued ?? DateTimeOffset.UtcNow;
        return new EntitlementSnapshot(
            userId,
            UserRole.User,
            plan,
            EmailVerified: true,
            BillingIsLive: true,
            Granted: new HashSet<MetisFeature> { MetisFeature.CustomAiProvider, MetisFeature.ManagedScreenVision },
            Limits: new PlanLimits(12m, 8_388_608, 25, 40, 2000, 60, 5000, ["gemini-2.5-flash"]),
            IssuedUtc: at,
            ExpiresUtc: at + EntitlementSigner.Lifetime);
    }

    [Fact]
    public void A_snapshot_survives_a_round_trip_intact()
    {
        var (privateKey, publicKey) = EntitlementSigner.GenerateKeyPair();
        using var signer = EntitlementSigner.TryLoadPrivateKey(privateKey)!;
        using var verifier = EntitlementSigner.TryLoadPublicKey(publicKey)!;

        var original = Snapshot();
        var restored = EntitlementSigner.Verify(
            EntitlementSigner.Sign(original, signer), verifier, "u_1", DateTimeOffset.UtcNow);

        Assert.NotNull(restored);
        Assert.Equal(PlanTier.Pro, restored!.Plan);
        Assert.True(restored.Has(MetisFeature.CustomAiProvider));
        Assert.Equal(12m, restored.Limits.MonthlyBudgetUsd);
        Assert.Equal(8_388_608, restored.Limits.MaxScreenshotBytes);
        Assert.Equal(["gemini-2.5-flash"], restored.Limits.ManagedModels);
    }

    /// <summary>
    /// The whole reason for the signature. Editing the plan in the cached blob
    /// has to fail, or the cache is just a file that grants subscriptions.
    /// </summary>
    [Fact]
    public void A_tampered_plan_is_rejected()
    {
        var (privateKey, publicKey) = EntitlementSigner.GenerateKeyPair();
        using var signer = EntitlementSigner.TryLoadPrivateKey(privateKey)!;
        using var verifier = EntitlementSigner.TryLoadPublicKey(publicKey)!;

        var signed = EntitlementSigner.Sign(Snapshot(plan: PlanTier.Free), signer);

        // Flip one byte of the payload half and re-encode it. The signature is
        // now over something else.
        var separator = signed.IndexOf('.');
        var payload = Convert.FromBase64String(Pad(signed[..separator]));
        payload[^1] ^= 0x01;
        var forged = ToBase64Url(payload) + signed[separator..];

        Assert.Null(EntitlementSigner.Verify(forged, verifier, "u_1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_signature_from_another_key_is_rejected()
    {
        var (privateKey, _) = EntitlementSigner.GenerateKeyPair();
        var (_, otherPublic) = EntitlementSigner.GenerateKeyPair();
        using var signer = EntitlementSigner.TryLoadPrivateKey(privateKey)!;
        using var stranger = EntitlementSigner.TryLoadPublicKey(otherPublic)!;

        Assert.Null(EntitlementSigner.Verify(
            EntitlementSigner.Sign(Snapshot(), signer), stranger, "u_1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_expired_snapshot_is_rejected()
    {
        var (privateKey, publicKey) = EntitlementSigner.GenerateKeyPair();
        using var signer = EntitlementSigner.TryLoadPrivateKey(privateKey)!;
        using var verifier = EntitlementSigner.TryLoadPublicKey(publicKey)!;

        var stale = Snapshot(issued: DateTimeOffset.UtcNow - EntitlementSigner.Lifetime - TimeSpan.FromDays(1));

        Assert.Null(EntitlementSigner.Verify(
            EntitlementSigner.Sign(stale, signer), verifier, "u_1", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Someone else's Pro snapshot, copied onto this machine, must do nothing.
    /// </summary>
    [Fact]
    public void A_snapshot_issued_for_another_account_is_rejected()
    {
        var (privateKey, publicKey) = EntitlementSigner.GenerateKeyPair();
        using var signer = EntitlementSigner.TryLoadPrivateKey(privateKey)!;
        using var verifier = EntitlementSigner.TryLoadPublicKey(publicKey)!;

        Assert.Null(EntitlementSigner.Verify(
            EntitlementSigner.Sign(Snapshot("u_someone_else"), signer),
            verifier,
            "u_1",
            DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-snapshot")]
    [InlineData("no-dot-separator")]
    [InlineData(".")]
    [InlineData("!!!.!!!")]
    public void Rubbish_is_rejected_without_throwing(string? value)
    {
        var (_, publicKey) = EntitlementSigner.GenerateKeyPair();
        using var verifier = EntitlementSigner.TryLoadPublicKey(publicKey)!;

        Assert.Null(EntitlementSigner.Verify(value, verifier, "u_1", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// A build with no compiled-in public key never trusts a cached plan and
    /// asks the server every time. That is correct behaviour, not a failure, so
    /// it must not throw.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all !!")]
    public void A_build_with_no_key_simply_has_no_key(string? key) =>
        Assert.Null(EntitlementSigner.TryLoadPublicKey(key));

    /// <summary>
    /// The offline window has to match the one the sign-in gate uses, or a user
    /// ends up signed out of an application that still believes they are on Pro.
    /// </summary>
    [Fact]
    public void The_offline_windows_agree() =>
        Assert.Equal(StartupAuthGate.OfflineGrace, EntitlementSigner.Lifetime);

    private static string Pad(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return padded + (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
