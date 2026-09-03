using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Metis.Core.Contracts;
using Metis.Core.Services;
using Xunit;

namespace Metis.Tests;

public sealed class UpdateServiceTests
{
    private sealed class TestLog : IDiagnosticLog
    {
        public string LogPath => string.Empty;
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Dispose() { }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    // The digest below is the one GitHub actually published for the live
    // v3.15.0 asset, so these pin the real shape rather than an invented one.
    private const string LiveRelease = """
    {
        "tag_name": "v3.15.0",
        "body": "What's new in v3.15.0. No checksum was pasted into this one.",
        "assets": [
            {
                "name": "Metis-Setup-3.15.0-win-x64.exe",
                "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v3.15.0/Metis-Setup-3.15.0-win-x64.exe",
                "digest": "sha256:17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64"
            }
        ]
    }
    """;

    [Fact]
    public void FindAssetDigest_reads_the_sha256_github_published()
    {
        using var doc = JsonDocument.Parse(LiveRelease);

        Assert.Equal(
            "17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64",
            UpdateService.FindAssetDigest(doc.RootElement));
    }

    [Fact]
    public void A_release_with_no_checksum_in_its_notes_still_gets_one()
    {
        // v3.15.0 shipped exactly like this: an installer, and release notes
        // nobody pasted a hash into. Before the digest fallback that meant the
        // download ran unverified.
        using var doc = JsonDocument.Parse(LiveRelease);
        var notes = doc.RootElement.GetProperty("body").GetString();

        Assert.Null(UpdateService.FindPublishedChecksum(notes));
        Assert.NotNull(UpdateService.FindAssetDigest(doc.RootElement));
    }

    [Fact]
    public void A_digest_in_another_algorithm_is_not_offered_as_a_sha256()
    {
        // Comparing a SHA-512 against a SHA-256 would fail every download
        // rather than skipping the check, which is worse than having none.
        var json = """
        {
            "assets": [
                {
                    "name": "Metis-Setup-9.0.0-win-x64.exe",
                    "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v9.0.0/Metis-Setup-9.0.0-win-x64.exe",
                    "digest": "sha512:17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);

        Assert.Null(UpdateService.FindAssetDigest(doc.RootElement));
    }

    [Fact]
    public void A_digest_on_an_asset_served_elsewhere_is_ignored()
    {
        // The host check guards the download; the digest must not be read off
        // an asset the downloader would have refused to fetch.
        var json = """
        {
            "assets": [
                {
                    "name": "Metis-Setup-9.0.0-win-x64.exe",
                    "browser_download_url": "https://example.com/Metis-Setup-9.0.0-win-x64.exe",
                    "digest": "sha256:17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);

        Assert.Null(UpdateService.FindInstaller(doc.RootElement));
        Assert.Null(UpdateService.FindAssetDigest(doc.RootElement));
    }

    [Fact]
    public void A_handwritten_checksum_wins_over_the_published_digest()
    {
        var json = """
        {
            "tag_name": "v9.0.0",
            "body": "SHA-256: aaaabbbbccccddddeeeeffff00001111222233334444555566667777888899990",
            "assets": [
                {
                    "name": "Metis-Setup-9.0.0-win-x64.exe",
                    "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v9.0.0/Metis-Setup-9.0.0-win-x64.exe",
                    "digest": "sha256:17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var notes = doc.RootElement.GetProperty("body").GetString();

        Assert.NotNull(UpdateService.FindPublishedChecksum(notes));
        Assert.NotEqual(UpdateService.FindPublishedChecksum(notes), UpdateService.FindAssetDigest(doc.RootElement));
    }

    [Fact]
    public void FindInstaller_accepts_valid_github_setup_executable()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Metis-Setup-3.8.0-win-x64.exe",
                    "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v3.8.0/Metis-Setup-3.8.0-win-x64.exe"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var uri = UpdateService.FindInstaller(doc.RootElement);

        Assert.NotNull(uri);
        Assert.Equal("https://github.com/Martinhaleluja/Metis/releases/download/v3.8.0/Metis-Setup-3.8.0-win-x64.exe", uri.ToString());
    }

    [Fact]
    public void FindInstaller_accepts_beta_setup_name()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Metis-Beta-Setup.exe",
                    "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v3.8.0/Metis-Beta-Setup.exe"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var uri = UpdateService.FindInstaller(doc.RootElement);

        Assert.NotNull(uri);
        Assert.Equal("https://github.com/Martinhaleluja/Metis/releases/download/v3.8.0/Metis-Beta-Setup.exe", uri.ToString());
    }

    [Fact]
    public void FindInstaller_rejects_untrusted_hosts()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Metis-Setup-3.8.0-win-x64.exe",
                    "browser_download_url": "https://malicious-site.com/Metis-Setup-3.8.0-win-x64.exe"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var uri = UpdateService.FindInstaller(doc.RootElement);

        Assert.Null(uri);
    }

    [Fact]
    public async Task CheckAsync_detects_newer_version_available()
    {
        var releaseJson = """
        {
            "tag_name": "v99.0.0",
            "name": "Metis 99.0.0",
            "draft": false,
            "body": "Major update released.",
            "assets": [
                {
                    "name": "Metis-Setup-99.0.0-win-x64.exe",
                    "browser_download_url": "https://github.com/Martinhaleluja/Metis/releases/download/v99.0.0/Metis-Setup-99.0.0-win-x64.exe"
                }
            ]
        }
        """;

        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, System.Text.Encoding.UTF8, "application/json")
        }));

        var service = new UpdateService(new TestLog(), client);
        var check = await service.CheckAsync();

        Assert.True(check.UpdateAvailable);
        Assert.Equal("99.0.0", check.Version);
        Assert.NotNull(check.Installer);
        Assert.Equal("Major update released.", check.Notes);
    }

    [Fact]
    public async Task CheckAsync_handles_404_gracefully_without_throwing()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var service = new UpdateService(new TestLog(), client);
        var check = await service.CheckAsync();

        Assert.False(check.UpdateAvailable);
        Assert.Contains("404", check.Problem, StringComparison.Ordinal);
    }
}
