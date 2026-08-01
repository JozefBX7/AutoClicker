using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class UiPreferencesStoreTests
{
    [TestMethod]
    public void Load_MissingFileUsesExpandedUnpinnedDefaults()
    {
        var path = TemporaryPath();
        try
        {
            var preferences = UiPreferencesStore.Load(path);
            Assert.IsFalse(preferences.Pinned);
            Assert.IsFalse(preferences.CompactMode);
            Assert.IsFalse(preferences.QuickStartSeen);
            Assert.AreEqual("Normal", preferences.WorkerPriority);
            Assert.IsFalse(preferences.CadenceDiagnosticsEnabled);
            Assert.IsFalse(preferences.AdvancedMode);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsWindowPreferences()
    {
        var path = TemporaryPath();
        try
        {
            UiPreferencesStore.Save(path, new UiPreferences { Pinned = true, CompactMode = true, QuickStartSeen = true, WorkerPriority = "AboveNormal", CadenceDiagnosticsEnabled = true, AdvancedMode = true });
            var preferences = UiPreferencesStore.Load(path);
            Assert.IsTrue(preferences.Pinned);
            Assert.IsTrue(preferences.CompactMode);
            Assert.IsTrue(preferences.QuickStartSeen);
            Assert.AreEqual("AboveNormal", preferences.WorkerPriority);
            Assert.IsTrue(preferences.CadenceDiagnosticsEnabled);
            Assert.IsTrue(preferences.AdvancedMode);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [DataTestMethod]
    [DataRow("Normal", WorkerPriorityOption.Normal)]
    [DataRow("AboveNormal", WorkerPriorityOption.AboveNormal)]
    [DataRow("abovenormal", WorkerPriorityOption.AboveNormal)]
    [DataRow("invalid", WorkerPriorityOption.Normal)]
    [DataRow(null, WorkerPriorityOption.Normal)]
    public void NormalizeWorkerPriority_UsesSupportedValuesAndDefaults(string? value, WorkerPriorityOption expected) =>
        Assert.AreEqual(expected, WorkerPriorityRules.Normalize(value));

    [TestMethod]
    public void Load_InvalidFileFallsBackToDefaults()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not json");
            var preferences = UiPreferencesStore.Load(path);
            Assert.IsFalse(preferences.Pinned);
            Assert.IsFalse(preferences.CompactMode);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "AutoClicker.Tests", Guid.NewGuid().ToString("N"), "ui-preferences.json");
    private static void DeleteTemporaryDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
