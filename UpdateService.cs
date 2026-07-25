using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AutoClicker;

internal sealed record UpdateCheckResult(bool IsUpdateAvailable, string? LatestTag, Uri? DownloadUri, string Message);

internal static class UpdateService
{
    internal const string Repository = "JozefBX7/AutoClicker";
    private static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");

    internal static Uri ReleasesUrl => ReleasesPage;

    internal static async Task<UpdateCheckResult> CheckForUpdateAsync(bool portable, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoClicker-update-check");
            using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, "Could not check GitHub Releases. Open Releases to download an update manually.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement))
                return new(false, null, null, "The latest GitHub release did not include a version tag.");

            var tag = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out var latest))
                return new(false, tag, null, "The latest GitHub release uses an unsupported version tag.");

            var current = CurrentVersion();
            if (latest <= current) return new(false, tag, null, $"You are up to date (v{current}).");
            return new(true, tag, DownloadUrl(tag, portable), $"Version {tag} is available (you have v{current}).");
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new(false, null, null, "Could not check GitHub Releases. Open Releases to download an update manually.");
        }
    }

    internal static Version CurrentVersion() => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    internal static Uri DownloadUrl(string tag, bool portable)
    {
        var asset = portable ? "AutoClicker-Portable-x64.zip" : "AutoClicker-Setup-x64.exe";
        return new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{asset}");
    }

    internal static bool TryParseVersion(string tag, out Version version) =>
        Version.TryParse(tag.Trim().TrimStart('v', 'V'), out version!);
}
