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
}
