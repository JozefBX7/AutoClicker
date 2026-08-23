// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class UpdateServiceTests
{
    [DataTestMethod]
    [DataRow("v1.2.3", "1.2.3")]
    [DataRow("V2.0.0", "2.0.0")]
    [DataRow(" 1.4.0 ", "1.4.0")]
    public void TryParseVersion_AcceptsReleaseTags(string tag, string expected)
    {
        Assert.IsTrue(UpdateService.TryParseVersion(tag, out var version));
        Assert.AreEqual(Version.Parse(expected), version);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("latest")]
    [DataRow("v1.two.3")]
    public void TryParseVersion_RejectsInvalidReleaseTags(string tag) =>
        Assert.IsFalse(UpdateService.TryParseVersion(tag, out _));

    [TestMethod]
    public void DownloadUrl_SelectsThePortableAsset() =>
        StringAssert.EndsWith(UpdateService.DownloadUrl("v1.2.3", portable: true).AbsoluteUri, "/v1.2.3/AutoClicker-Portable-x64.zip");

    [TestMethod]
    public void DownloadUrl_SelectsTheInstallerAsset() =>
        StringAssert.EndsWith(UpdateService.DownloadUrl("v1.2.3", portable: false).AbsoluteUri, "/v1.2.3/AutoClicker-Setup-x64.exe");

    [TestMethod]
    public void IsOfficialDownloadUrl_AcceptsOnlyThisRepositorysGithubReleaseAssets()
    {
        Assert.IsTrue(UpdateService.IsOfficialDownloadUrl(UpdateService.DownloadUrl("v1.2.3", portable: false)));
        Assert.IsFalse(UpdateService.IsOfficialDownloadUrl(new Uri("https://example.com/AutoClicker-Setup-x64.exe")));
        Assert.IsFalse(UpdateService.IsOfficialDownloadUrl(new Uri("https://github.com/other/AutoClicker/releases/download/v1.2.3/AutoClicker-Setup-x64.exe")));
        Assert.IsFalse(UpdateService.IsOfficialDownloadUrl(new Uri("https://github.com/JozefBX7/AutoClicker/releases/download/v1.2.3/extra/AutoClicker-Setup-x64.exe")));
        Assert.IsFalse(UpdateService.IsOfficialDownloadUrl(new Uri("https://github.com/JozefBX7/AutoClicker/releases/download/v1.2.3/AutoClicker-Setup-x64.exe?asset=other")));
        Assert.IsFalse(UpdateService.IsOfficialDownloadUrl(
            UpdateService.DownloadUrl("v1.2.3", portable: false),
            "v1.2.4",
            UpdateService.InstallerAssetName));
    }

    [TestMethod]
    public void UpdateCheckResult_StoresReleaseNotes()
    {
        var result = new UpdateCheckResult(true, "v1.2.3", UpdateService.DownloadUrl("v1.2.3", portable: false), "Update available.", "- Added release notes");

        Assert.AreEqual("- Added release notes", result.ReleaseNotes);
    }

    [TestMethod]
    public void FullChangelogReleaseNotes_ProvideFriendlyTextAndLink()
    {
        const string releaseNotes = "**Full Changelog**: https://github.com/JozefBX7/AutoClicker/commits/v1.0.0";
        var url = UpdateService.TryGetReleaseNotesUrl(releaseNotes);

        Assert.IsNotNull(url);
        Assert.AreEqual("https://github.com/JozefBX7/AutoClicker/commits/v1.0.0", url.AbsoluteUri);
        Assert.AreEqual("This release provides a full changelog.", UpdateService.FormatReleaseNotes(releaseNotes, url));
    }

    [TestMethod]
    public void ReleaseHistoryEntry_StoresConciseReleaseDetails()
    {
        var entry = new ReleaseHistoryEntry("v1.2.3", "- Improved update history", null);

        Assert.AreEqual("v1.2.3", entry.Tag);
        Assert.AreEqual("- Improved update history", entry.Notes);
        Assert.IsNull(entry.DetailsUrl);
    }

    [TestMethod]
    public void ParseReleaseResponse_SelectsHighestStableVersionAndVerifiedAssetMetadata()
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var json = $$"""
            [
              {
                "tag_name": "v1.2.0",
                "draft": false,
                "prerelease": false,
                "body": "Older",
                "assets": []
              },
              {
                "tag_name": "v1.3.0",
                "draft": false,
                "prerelease": false,
                "body": "Current",
                "assets": [{
                  "name": "AutoClicker-Setup-x64.exe",
                  "browser_download_url": "https://github.com/JozefBX7/AutoClicker/releases/download/v1.3.0/AutoClicker-Setup-x64.exe",
                  "size": 12345,
                  "digest": "sha256:{{digest}}"
                }]
              },
              {
                "tag_name": "v9.0.0",
                "draft": false,
                "prerelease": true,
                "body": "Preview",
                "assets": []
              }
            ]
            """;

        var result = UpdateService.ParseReleaseResponse(json, portable: false, Version.Parse("1.1.0"));

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("v1.3.0", result.LatestTag);
        Assert.AreEqual(12345L, result.DownloadSize);
        Assert.AreEqual(digest, result.DownloadSha256);
        Assert.AreEqual(UpdateService.DownloadUrl("v1.3.0", portable: false), result.DownloadUri);
        CollectionAssert.AreEqual(new[] { "v1.3.0", "v1.2.0" }, result.RecentReleases!.Select(entry => entry.Tag).ToArray());
    }

    [TestMethod]
    public void ParseReleaseResponse_DoesNotOfferAutomaticInstallWithoutIntegrityMetadata()
    {
        const string json = """
            [{
              "tag_name": "v2.0.0",
              "draft": false,
              "prerelease": false,
              "assets": [{
                "name": "AutoClicker-Setup-x64.exe",
                "browser_download_url": "https://github.com/JozefBX7/AutoClicker/releases/download/v2.0.0/AutoClicker-Setup-x64.exe",
                "size": 12345,
                "digest": null
              }]
            }]
            """;

        var result = UpdateService.ParseReleaseResponse(json, portable: false, Version.Parse("1.0.0"));

        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsNull(result.DownloadUri);
        StringAssert.Contains(result.Message, "verified installer is unavailable");
    }

    [DataTestMethod]
    [DataRow("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [DataRow("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [DataRow("sha256:not-a-digest", false)]
    [DataRow("", false)]
    public void TryParseSha256_RequiresACompleteHexDigest(string digest, bool expected) =>
        Assert.AreEqual(expected, UpdatePackageDownloader.TryParseSha256(digest, out _));

    [TestMethod]
    public async Task DownloadInstallerAsync_PromotesOnlyACompleteVerifiedWindowsExecutable()
    {
        var installer = CreatePortableExecutable();
        var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "AutoClicker-Setup-x64-old.exe.partial-stale"), "stale");
            using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installer)
            }));

            var path = await UpdatePackageDownloader.DownloadInstallerAsync(
                UpdateService.DownloadUrl("v3.0.0", portable: false),
                "v3.0.0",
                installer.Length,
                digest,
                client,
                directory,
                CancellationToken.None);

            CollectionAssert.AreEqual(installer, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(0, Directory.EnumerateFiles(directory, "*.partial-*").Count());
            await UpdatePackageDownloader.VerifyInstallerAsync(path, installer.Length, digest, CancellationToken.None);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_DeletesPartialFileWhenIntegrityVerificationFails()
    {
        var installer = CreatePortableExecutable();
        var wrongDigest = new string('0', 64);
        var directory = CreateTemporaryDirectory();
        try
        {
            using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installer)
            }));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => UpdatePackageDownloader.DownloadInstallerAsync(
                UpdateService.DownloadUrl("v3.0.1", portable: false),
                "v3.0.1",
                installer.Length,
                wrongDigest,
                client,
                directory,
                CancellationToken.None));

            Assert.AreEqual(0, Directory.EnumerateFiles(directory).Count(), "an unverified installer or partial download was left behind");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_RejectsNonExecutableContentEvenWhenItsHashMatches()
    {
        var content = new byte[256];
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var directory = CreateTemporaryDirectory();
        try
        {
            using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            }));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => UpdatePackageDownloader.DownloadInstallerAsync(
                UpdateService.DownloadUrl("v3.0.2", portable: false),
                "v3.0.2",
                content.Length,
                digest,
                client,
                directory,
                CancellationToken.None));

            Assert.AreEqual(0, Directory.EnumerateFiles(directory).Count());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_CancellationRemovesThePartialDownload()
    {
        var installer = CreatePortableExecutable();
        var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var directory = CreateTemporaryDirectory();
        try
        {
            using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptibleStream(installer))
            }));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => UpdatePackageDownloader.DownloadInstallerAsync(
                UpdateService.DownloadUrl("v3.0.3", portable: false),
                "v3.0.3",
                installer.Length,
                digest,
                client,
                directory,
                cancellation.Token));

            Assert.AreEqual(0, Directory.EnumerateFiles(directory).Count(), "the cancelled partial download was not removed");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] CreatePortableExecutable()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(128).CopyTo(bytes, 0x3C);
        bytes[128] = (byte)'P';
        bytes[129] = (byte)'E';
        return bytes;
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoClickerUpdaterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class InterruptibleStream(byte[] content) : Stream
    {
        private bool suppliedFirstChunk;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!suppliedFirstChunk)
            {
                suppliedFirstChunk = true;
                var count = Math.Min(64, Math.Min(buffer.Length, content.Length));
                content.AsMemory(0, count).CopyTo(buffer);
                return count;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
