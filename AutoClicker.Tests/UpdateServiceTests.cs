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
}
