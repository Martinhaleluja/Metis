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
