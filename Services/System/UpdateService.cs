// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.IO;

namespace AutoClicker;

internal sealed record ReleaseHistoryEntry(string Tag, string Notes, Uri? DetailsUrl);

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string? LatestTag,
    Uri? DownloadUri,
    string Message,
    string? ReleaseNotes = null,
    Uri? ReleaseNotesUrl = null,
    IReadOnlyList<ReleaseHistoryEntry>? RecentReleases = null);

internal static class UpdateService
{
    internal const string Repository = "JozefBX7/AutoClicker";
    private static readonly Uri RecentReleasesApi = new($"https://api.github.com/repos/{Repository}/releases?per_page=3");
    private static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");

    internal static Uri ReleasesUrl => ReleasesPage;

    internal static async Task<UpdateCheckResult> CheckForUpdateAsync(bool portable, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoClicker-update-check");
            using var response = await client.GetAsync(RecentReleasesApi, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(false, null, null, "No published GitHub Release is available yet.");
            if (!response.IsSuccessStatusCode)
                return new(false, null, null, "Could not check GitHub Releases. Open Releases to download an update manually.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new(false, null, null, "GitHub Releases returned an unexpected response.");

            var releases = new List<ReleaseHistoryEntry>();
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draftElement) && draftElement.GetBoolean()
                    || release.TryGetProperty("prerelease", out var prereleaseElement) && prereleaseElement.GetBoolean()
                    || !release.TryGetProperty("tag_name", out var tagElement)) continue;

                var tag = tagElement.GetString();
                if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out _)) continue;

                var releaseNotes = release.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
                var releaseNotesUrl = TryGetReleaseNotesUrl(releaseNotes);
                releases.Add(new ReleaseHistoryEntry(tag, FormatReleaseNotes(releaseNotes, releaseNotesUrl) ?? "No release notes were provided.", releaseNotesUrl));
            }

            if (releases.Count == 0)
                return new(false, null, null, "No published GitHub Release is available yet.");

            var latestRelease = releases[0];
            _ = TryParseVersion(latestRelease.Tag, out var latest);
            var current = CurrentVersion();
            if (latest <= current)
                return new(false, latestRelease.Tag, null, $"You are up to date (v{current}).", RecentReleases: releases);

            return new(true, latestRelease.Tag, DownloadUrl(latestRelease.Tag, portable), $"Version {latestRelease.Tag} is available (you have v{current}).", latestRelease.Notes, latestRelease.DetailsUrl, releases);
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

    // Only install assets from this repository's release path.
    internal static bool IsOfficialDownloadUrl(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal);

    internal static async Task<string> DownloadInstallerAsync(Uri downloadUri, string versionTag, CancellationToken cancellationToken)
    {
        if (!IsOfficialDownloadUrl(downloadUri))
            throw new InvalidOperationException("The update download is not an official AutoClicker GitHub Release.");

        // The tag is used in a temporary filename.
        var safeTag = string.Concat(versionTag.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeTag)) safeTag = "latest";
        var directory = Path.Combine(Path.GetTempPath(), "AutoClicker", "Updates");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"AutoClicker-Setup-x64-{safeTag}.exe");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoClicker-update-installer");
        using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, cancellationToken);
        return destination;
    }
}
