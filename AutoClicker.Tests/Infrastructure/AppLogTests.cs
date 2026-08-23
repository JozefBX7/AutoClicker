// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AppLogTests
{
    private const string TestDirectoryName = "AutoClicker.Tests";
    [TestMethod]
    public void TryPrepareForAppend_RotatesAndCapsThePreviousLog()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[AppLog.MaxLogBytes + 64]);

            Assert.IsTrue(AppLog.TryPrepareForAppend(path));

            var previous = Path.Combine(Path.GetDirectoryName(path)!, "AutoClicker.previous.log");
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists(previous));
            Assert.IsTrue(new FileInfo(previous).Length <= AppLog.MaxLogBytes);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void TryPrepareForAppend_WhenCappedLogIsLocked_ReturnsFalseWithoutThrowing()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[AppLog.MaxLogBytes]);
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            Assert.IsFalse(AppLog.TryPrepareForAppend(path));
            Assert.AreEqual(AppLog.MaxLogBytes, new FileInfo(path).Length);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void TryPrepareForAppend_RotatesBeforeAnEntryWouldCrossTheCap()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[AppLog.MaxLogBytes - 1]);

            Assert.IsTrue(AppLog.TryPrepareForAppend(path, bytesToAppend: 2));
            Assert.IsFalse(File.Exists(path));
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), TestDirectoryName, Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat), ConfigurationFileNames.Log);

    private static void DeleteTemporaryDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
