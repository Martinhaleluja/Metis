using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Metis.Core.Contracts;
using Metis.Core.Services;

namespace Metis.App.Runtime;

/// <summary>What a check against the release feed found.</summary>
public sealed record UpdateCheck(
    bool UpdateAvailable,
    string? Version = null,
    Uri? Installer = null,
    string? Notes = null,
    string? Problem = null);

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
public sealed class UpdateService(IDiagnosticLog log)
{
    /// <summary>The repository releases are published from.</summary>
    private const string ReleasesApi = "https://api.github.com/repos/Martinhaleluja/Metis/releases/latest";

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

    /// <summary>Asks GitHub what the newest release is, and whether it beats this build.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Metis", AppVersion.Current));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.GetAsync(ReleasesApi, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // A repository with no releases yet answers 404. That is the
                // ordinary state before the first one is published, not a fault
                // worth showing anyone.
                return new UpdateCheck(false, Problem: $"The release feed answered {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
            return new UpdateCheck(true, AppVersion.Parse(tag)?.ToString(3), installer, notes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An update check must never be the reason Metis fails to start or
            // stops working. Being unable to reach GitHub is an ordinary state.
            return new UpdateCheck(false, Problem: exception.Message);
        }
    }

    /// <summary>
    /// Picks the Windows installer out of a release's assets, rejecting
    /// anything served from somewhere other than GitHub.
    /// </summary>
    private static Uri? FindInstaller(JsonElement release)
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

            if (!(name.StartsWith("Metis-Setup-", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Metis.exe", StringComparison.OrdinalIgnoreCase))
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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
    public async Task<bool> DownloadAndRunAsync(UpdateCheck check, CancellationToken cancellationToken = default)
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

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
            {
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Metis", AppVersion.Current));

                using var response = await http.GetAsync(
                    check.Installer, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                // Written beside the final name and moved into place, so a
                // download cut off half way cannot leave a truncated installer
                // that looks complete on the next launch.
                var partial = target + ".part";
                await using (var file = File.Create(partial))
                {
                    await response.Content.CopyToAsync(file, cancellationToken);
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(partial, target);
            }

            _log.Info($"Update {check.Version} downloaded to {target}. Starting the installer.");

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
