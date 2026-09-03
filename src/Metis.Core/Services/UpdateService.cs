using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using Metis.Core.Models;
using System.Threading.Tasks;
using Metis.Core.Contracts;

namespace Metis.Core.Services;

/// <summary>What a check against the release feed found.</summary>
public sealed record UpdateCheck(
    bool UpdateAvailable,
    string? Version = null,
    Uri? Installer = null,
    string? Notes = null,
    string? Problem = null,

    /// <summary>
    /// The SHA-256 the downloaded installer must match, when the release
    /// publishes one. Null means the release said nothing, which is not the
    /// same as saying the file is fine.
    /// </summary>
    string? Sha256 = null);

/// <summary>
/// Keeps testers on the newest build without anyone having to hand them an
/// installer.
///
/// It works by running the ordinary Metis installer again. That is the whole
/// trick, and it works because the installer was already built for it: the
/// AppId is stable, UsePreviousAppDir keeps the location, CloseApplications
/// shuts the running copy, and the install is per-user under LOCALAPPDATA with
/// PrivilegesRequired=lowest — so an upgrade needs no administrator rights and
/// raises no consent prompt. There is no separate update mechanism to keep
/// correct alongside the installer, which is the usual way these rot.
///
/// Releases are read from the GitHub API rather than a server of Metis's own,
/// because release hosting on a public repository is free and has no bandwidth
/// cap, and because it means there is no update service that can go down.
/// </summary>
public sealed class UpdateService(IDiagnosticLog log, HttpClient? httpClient = null)
{
    /// <summary>The repository releases are published from.</summary>
    public const string DefaultReleasesApi = "https://api.github.com/repos/Martinhaleluja/Metis/releases/latest";

    /// <summary>
    /// Only assets served by GitHub are ever fetched. The release feed is
    /// public JSON naming a URL to download and run, so the host it points at
    /// is checked rather than trusted: a redirect to somewhere else is the one
    /// thing that would turn this from an updater into a delivery mechanism.
    /// </summary>
    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    private readonly IDiagnosticLog _log = log;
    private readonly HttpClient? _injectedClient = httpClient;

    /// <summary>Asks GitHub what the newest release is, and whether it beats this build.</summary>
    public async Task<UpdateCheck> CheckAsync(
        string releasesApi = DefaultReleasesApi,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var http = _injectedClient;
            var ownsClient = false;
            if (http is null)
            {
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                ownsClient = true;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, releasesApi);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Metis", AppVersion.Current));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // A repository with no releases yet answers 404. That is the
                    // ordinary state before the first one is published, not a fault
                    // worth showing anyone.
                    return new UpdateCheck(false, Problem: $"The release feed answered {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;

                if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                {
                    return new UpdateCheck(false, Problem: "The newest release is still a draft.");
                }

                var tag = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;

                if (!AppVersion.IsNewer(tag, AppVersion.Current))
                {
                    return new UpdateCheck(false);
                }

                var installer = FindInstaller(root);
                if (installer is null)
                {
                    return new UpdateCheck(false, Problem: $"Release {tag} carries no Metis installer.");
                }

                var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
                return new UpdateCheck(
                    true,
                    AppVersion.Parse(tag)?.ToString(3),
                    installer,
                    notes,
                    Sha256: FindPublishedChecksum(notes));
            }
            finally
            {
                if (ownsClient)
                {
                    http.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An update check must never be the reason Metis fails to start or
            // stops working. Being unable to reach GitHub is an ordinary state.
            return new UpdateCheck(false, Problem: exception.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256
            .HashDataAsync(file, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Reads the installer's SHA-256 out of the release notes.
    ///
    /// Metis downloads an executable and runs it with no prompt, which is
    /// exactly the shape of a supply-chain problem: anyone who could replace
    /// that asset would be running code on every machine that has Metis
    /// installed, under its name. The transport is HTTPS to GitHub, which is
    /// worth something, but it is not a check on the file itself.
    ///
    /// The notes carry it rather than a second asset so publishing a release
    /// stays one upload, and the line the build script writes is the line this
    /// looks for.
    /// </summary>
    public static string? FindPublishedChecksum(string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            releaseNotes,
            "SHA-?256[^A-Fa-f0-9]{0,20}([A-Fa-f0-9]{64})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Picks the Windows installer out of a release's assets, rejecting
    /// anything served from somewhere other than GitHub.
    /// </summary>
    public static Uri? FindInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            if (name is null || url is null)
            {
                continue;
            }

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !name.Contains("Metis", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return uri;
        }

        return null;
    }

    /// <summary>
    /// Downloads the installer and starts it.
    ///
    /// Started with Inno's /SILENT rather than /VERYSILENT: the user sees a
    /// progress window and knows why Metis just closed itself, which /VERYSILENT
    /// would leave looking like a crash.
    /// </summary>
    public async Task<bool> DownloadAndRunAsync(
        UpdateCheck check,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!check.UpdateAvailable || check.Installer is null)
        {
            return false;
        }

        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "Metis", "updates");
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, $"Metis-Setup-{check.Version ?? "latest"}-win-x64.exe");

            var http = _injectedClient;
            var ownsClient = false;
            if (http is null)
            {
                http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                ownsClient = true;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, check.Installer);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Metis", AppVersion.Current));

                using var response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                // Absent when the server does not send one, which is a real
                // case rather than a fault — the indicator shows an
                // indeterminate state instead of inventing a percentage.
                var total = response.Content.Headers.ContentLength;
                progress?.Report(new UpdateProgress(UpdatePhase.Downloading, 0, total));

                // Written beside the final name and moved into place, so a
                // download cut off half way cannot leave a truncated installer
                // that looks complete on the next launch.
                var partial = target + ".part";
                await using (var file = File.Create(partial))
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                    // CopyToAsync would be shorter and reports nothing at all,
                    // which is how this came to have no feedback in the first
                    // place. The loop is the whole point.
                    var buffer = new byte[81920];
                    long read = 0;
                    long reportedAt = 0;
                    var lastReport = DateTimeOffset.UtcNow;

                    while (true)
                    {
                        var count = await stream
                            .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                        if (count == 0)
                        {
                            break;
                        }

                        await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        read += count;

                        // Throttled to whichever comes first, a quarter of a
                        // megabyte or a tenth of a second. Reporting every chunk
                        // would marshal to the interface thread faster than it
                        // can repaint, and the marshalling would cost more than
                        // the download.
                        var now = DateTimeOffset.UtcNow;
                        if (read - reportedAt >= 262_144 || (now - lastReport).TotalMilliseconds >= 100)
                        {
                            progress?.Report(new UpdateProgress(UpdatePhase.Downloading, read, total));
                            reportedAt = read;
                            lastReport = now;
                        }
                    }

                    progress?.Report(new UpdateProgress(UpdatePhase.Downloading, read, total ?? read));
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(partial, target);
            }
            finally
            {
                if (ownsClient)
                {
                    http.Dispose();
                }
            }

            // The file is about to be executed, so this is the last moment
            // anything can check it is the file that was published. Reported as
            // its own phase because on a large installer it takes long enough to
            // look like a hang immediately after the bar filled.
            progress?.Report(new UpdateProgress(UpdatePhase.Verifying));
            var actual = await ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);
            if (check.Sha256 is { Length: 64 } expected)
            {
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    _log.Error(
                        $"The downloaded installer for {check.Version} did not match the checksum in the release " +
                        $"(expected {expected}, got {actual}). It was deleted and not run.");
                    return false;
                }

                _log.Info($"Update {check.Version} matches the published checksum. Starting the installer.");
            }
            else
            {
                // Not a reason to refuse the update — that would break every
                // release published before checksums existed — but it is a
                // reason to say so, because it is the difference between a
                // verified installer and one that is merely well-transported.
                _log.Info(
                    $"Update {check.Version} publishes no SHA-256, so the download could not be verified " +
                    $"(its hash is {actual}). Starting the installer.");
            }

            progress?.Report(new UpdateProgress(UpdatePhase.Starting));

            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS"
            });

            return true;
        }
        catch (Exception exception)
        {
            _log.Error($"The update to {check.Version} could not be installed.", exception);
            return false;
        }
    }
}
