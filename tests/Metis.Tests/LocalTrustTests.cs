using Metis.Core.Agents;
using Metis.Core.Services;
using Metis.Data;

namespace Metis.Tests;

/// <summary>
/// The guardrails that matter when something goes wrong: what Metis leaves on
/// disk, what an agent is allowed to touch, what ends up in a log a user might
/// send to a stranger, and whether an update is the file it claims to be.
/// </summary>
public sealed class LocalTrustTests
{
    // ----- what Metis leaves on disk -----

    [Fact]
    public void A_stored_record_round_trips_and_is_not_readable_as_text()
    {
        const string secret = "The user asked about their bank balance.";

        var stored = LocalVault.Protect(secret);

        Assert.True(LocalVault.IsProtected(stored));
        Assert.DoesNotContain("bank balance", System.Text.Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
        Assert.Equal(secret, LocalVault.Unprotect(stored));
    }

    /// <summary>
    /// A chat written before Metis encrypted anything still has to open, or
    /// upgrading would silently throw away the user's history.
    /// </summary>
    [Fact]
    public void A_document_from_before_encryption_still_opens()
    {
        var plain = System.Text.Encoding.UTF8.GetBytes("{\"Id\":\"old\"}");

        Assert.False(LocalVault.IsProtected(plain));
        Assert.Equal("{\"Id\":\"old\"}", LocalVault.Unprotect(plain));
    }

    [Fact]
    public void A_record_that_will_not_decrypt_reports_nothing_rather_than_throwing()
    {
        var stored = LocalVault.Protect("something");

        // Corrupt the ciphertext but keep the marker, which is what a file
        // written by another Windows account looks like from here.
        stored[^1] ^= 0xFF;
        stored[^2] ^= 0xFF;

        Assert.Null(LocalVault.Unprotect(stored));
    }

    // ----- what an agent may touch -----

    [Theory]
    [InlineData(@"C:\Users\someone\.ssh\id_rsa")]
    [InlineData(@"C:\Users\someone\.aws\credentials")]
    [InlineData(@"C:\Users\someone\AppData\Local\Google\Chrome\User Data\Default\Login Data")]
    [InlineData(@"C:\Users\someone\AppData\Roaming\Mozilla\Firefox\Profiles\abc\key4.db")]
    [InlineData(@"C:\Users\someone\AppData\Roaming\Microsoft\Credentials\DFBFE0")]
    [InlineData(@"C:\Users\someone\project\.env")]
    [InlineData(@"C:\Users\someone\project\.env.production")]
    [InlineData(@"C:\Users\someone\.git-credentials")]
    [InlineData(@"C:\Users\someone\AppData\Local\Metis\chats\one.json")]
    public void Credential_stores_are_refused_even_when_the_agent_may_leave_its_workspace(string path)
    {
        var decision = AgentWorkspace.Resolve(@"C:\workspace", path, allowOutside: true);

        Assert.Null(decision.FullPath);
        Assert.Contains("off limits", decision.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_work_is_still_allowed()
    {
        var decision = AgentWorkspace.Resolve(@"C:\workspace", "notes.md");

        Assert.NotNull(decision.FullPath);
        Assert.Null(decision.DenialReason);
    }

    /// <summary>
    /// The deny-list is matched on the resolved path, so climbing out of the
    /// workspace with ".." reaches it just the same.
    /// </summary>
    [Fact]
    public void A_relative_climb_out_to_a_credential_store_is_refused()
    {
        var decision = AgentWorkspace.Resolve(
            @"C:\Users\someone\work",
            @"..\.ssh\id_ed25519",
            allowOutside: true);

        Assert.Null(decision.FullPath);
    }

    // ----- what reaches the log -----

    [Theory]
    [InlineData("sk-ant-api03-" + "abcdefghij0123456789abcdefghij", "sk-ant-")]
    [InlineData("sk-proj-" + "abcdefghij0123456789abcdefghij", "sk-proj-")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r", "eyJhbGciOi")]
    public void Secret_shapes_never_reach_the_log(string secret, string tellTale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Metis.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var log = new FileDiagnosticLog(directory);
            log.Error($"the provider refused: {secret}");
            log.Flush();

            var contents = File.ReadAllText(log.LogPath);
            Assert.DoesNotContain(secret, contents, StringComparison.Ordinal);
            Assert.DoesNotContain(tellTale, contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_key_sent_as_a_header_is_redacted_by_its_header_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Metis.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var log = new FileDiagnosticLog(directory);
            log.Error("request failed: xi-api-key: abcd1234efgh5678");
            log.Flush();

            var contents = File.ReadAllText(log.LogPath);
            Assert.DoesNotContain("abcd1234efgh5678", contents, StringComparison.Ordinal);
            Assert.Contains("[redacted]", contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ----- whether an update is the file it claims to be -----

    [Fact]
    public void A_published_checksum_is_read_out_of_the_release_notes()
    {
        const string hash = "9f2c1b7ae4d05836af11c9d2e7b430aa5c6d8e1f2039485760bacdef01234567";

        Assert.Equal(hash, UpdateService.FindPublishedChecksum($"Bug fixes.\n\nSHA-256: {hash}\n"));
        Assert.Equal(hash, UpdateService.FindPublishedChecksum($"sha256 {hash.ToUpperInvariant()}"));
        Assert.Null(UpdateService.FindPublishedChecksum("No checksum here."));
        Assert.Null(UpdateService.FindPublishedChecksum(null));
    }
}
