// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AutoClicker;

internal sealed record ReleaseHistoryEntry(string Tag, string Notes, Uri? DetailsUrl);

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string? LatestTag,
    Uri? DownloadUri,
    string Message,
    string? ReleaseNotes = null,
    Uri? ReleaseNotesUrl = null,
    IReadOnlyList<ReleaseHistoryEntry>? RecentReleases = null,
    long? DownloadSize = null,
    string? DownloadSha256 = null);

internal static class UpdateService
{
    private const string RepositoryOwner = "JozefBX7";
    private const string RepositoryName = "AutoClicker";
    internal const string Repository = RepositoryOwner + "/" + RepositoryName;
    internal const string InstallerAssetName = "AutoClicker-Setup-x64.exe";
    internal const string PortableAssetName = "AutoClicker-Portable-x64.zip";
    private const int RecentReleaseLimit = 3;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri RecentReleasesApi = new($"https://api.github.com/repos/{Repository}/releases?per_page=10");
    private static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static Uri ReleasesUrl => ReleasesPage;

    internal static async Task<UpdateCheckResult> CheckForUpdateAsync(bool portable, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            using var response = await HttpClient.GetAsync(RecentReleasesApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(false, null, null, "No published GitHub Release is available yet.");
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, "Could not check GitHub Releases. Open Releases to download an update manually.");

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseReleaseResponse(json, portable, CurrentVersion());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            AppLog.Error("GitHub release check failed", exception);
            return new(false, null, null, "Could not check GitHub Releases. Open Releases to download an update manually.");
        }
    }

    internal static UpdateCheckResult ParseReleaseResponse(string json, bool portable, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new(false, null, null, "GitHub Releases returned an unexpected response.");

        var candidates = new List<ReleaseCandidate>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (ReadBoolean(release, "draft") || ReadBoolean(release, "prerelease")
                || !release.TryGetProperty("tag_name", out var tagElement)) continue;

            var tag = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out var version)) continue;

            var releaseNotes = release.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString()
                : null;
            var releaseNotesUrl = TryGetReleaseNotesUrl(releaseNotes);
            var history = new ReleaseHistoryEntry(
                tag,
                FormatReleaseNotes(releaseNotes, releaseNotesUrl) ?? "No release notes were provided.",
                releaseNotesUrl);
            candidates.Add(new ReleaseCandidate(version, tag, history, ReadReleaseAsset(release, tag, portable)));
        }

        if (candidates.Count == 0)
            return new(false, null, null, "No published GitHub Release is available yet.");

        var ordered = candidates.OrderByDescending(candidate => candidate.Version).ToArray();
        var latest = ordered[0];
        var historyEntries = ordered.Take(RecentReleaseLimit).Select(candidate => candidate.History).ToArray();
        if (latest.Version <= currentVersion)
            return new(false, latest.Tag, null, $"You are up to date (v{currentVersion}).", RecentReleases: historyEntries);

        if (latest.Asset is null)
            return new(
                false,
                latest.Tag,
                null,
                $"Version {latest.Tag} is published, but its verified {(portable ? "portable archive" : "installer")} is unavailable. Open Releases to update manually.",
                latest.History.Notes,
                latest.History.DetailsUrl,
                historyEntries);

        return new(
            true,
            latest.Tag,
            latest.Asset.DownloadUri,
            $"Version {latest.Tag} is available (you have v{currentVersion}).",
            latest.History.Notes,
            latest.History.DetailsUrl,
            historyEntries,
            latest.Asset.Size,
            latest.Asset.Sha256);
    }

    internal static Version CurrentVersion() => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    internal static Uri DownloadUrl(string tag, bool portable)
    {
        var asset = portable ? PortableAssetName : InstallerAssetName;
        return new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{asset}");
    }

    internal static bool TryParseVersion(string tag, out Version version) =>
        Version.TryParse(tag.Trim().TrimStart('v', 'V'), out version!);

    internal static string? FormatReleaseNotes(string? releaseNotes, Uri? releaseNotesUrl)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes)) return null;

        var formattedNotes = releaseNotes.Replace("**", string.Empty).Trim();
        return releaseNotesUrl is not null && formattedNotes.Equals($"Full Changelog: {releaseNotesUrl}", StringComparison.OrdinalIgnoreCase)
            ? "This release provides a full changelog."
            : formattedNotes;
    }

    internal static Uri? TryGetReleaseNotesUrl(string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes)) return null;

        var urlStart = releaseNotes.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (urlStart < 0) return null;

        var urlText = releaseNotes[urlStart..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        return Uri.TryCreate(urlText, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
    }

    internal static bool IsOfficialDownloadUrl(Uri uri) =>
        IsOfficialDownloadUrl(uri, expectedTag: null, expectedAssetName: null);

    internal static bool IsOfficialDownloadUrl(Uri uri, string? expectedTag, string? expectedAssetName)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 6
            || !segments[0].Equals(RepositoryOwner, StringComparison.Ordinal)
            || !segments[1].Equals(RepositoryName, StringComparison.Ordinal)
            || !segments[2].Equals("releases", StringComparison.Ordinal)
            || !segments[3].Equals("download", StringComparison.Ordinal)) return false;

        var tag = Uri.UnescapeDataString(segments[4]);
        var asset = Uri.UnescapeDataString(segments[5]);
        if (!TryParseVersion(tag, out _)) return false;
        if (expectedTag is not null && !tag.Equals(expectedTag, StringComparison.Ordinal)) return false;
        if (expectedAssetName is not null && !asset.Equals(expectedAssetName, StringComparison.Ordinal)) return false;
        return asset is InstallerAssetName or PortableAssetName;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoClicker-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True;

    private static ReleaseAsset? ReadReleaseAsset(JsonElement release, string tag, bool portable)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var expectedName = portable ? PortableAssetName : InstallerAssetName;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement)
                || !expectedName.Equals(nameElement.GetString(), StringComparison.Ordinal)
                || !asset.TryGetProperty("browser_download_url", out var urlElement)
                || !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var downloadUri)
                || !IsOfficialDownloadUrl(downloadUri, tag, expectedName)
                || !asset.TryGetProperty("size", out var sizeElement)
                || !sizeElement.TryGetInt64(out var size)
                || size <= 0
                || (!portable && size > UpdatePackageDownloader.MaximumInstallerBytes)
                || !asset.TryGetProperty("digest", out var digestElement)
                || digestElement.ValueKind != JsonValueKind.String
                || !UpdatePackageDownloader.TryParseSha256(digestElement.GetString() ?? string.Empty, out var hash)) continue;

            return new ReleaseAsset(downloadUri, size, Convert.ToHexString(hash).ToLowerInvariant());
        }
        return null;
    }

    private sealed record ReleaseCandidate(Version Version, string Tag, ReleaseHistoryEntry History, ReleaseAsset? Asset);
    private sealed record ReleaseAsset(Uri DownloadUri, long Size, string Sha256);
}
