// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class ApplicationPreferencesStoreTests
{
    private const string TestDirectoryName = "AutoClicker.Tests";

    [TestMethod]
    public void Load_MissingFileUsesSafeApplicationDefaults()
    {
        var path = TemporaryPath(ConfigurationFileNames.ApplicationPreferences);
        try
        {
            var preferences = ApplicationPreferencesStore.Load(path);
            Assert.IsFalse(preferences.Pinned);
            Assert.IsTrue(preferences.RememberPinned);
            Assert.IsTrue(preferences.ApplyPinnedOnLaunch);
            Assert.IsFalse(preferences.CompactMode);
            Assert.IsFalse(preferences.QuickStartSeen);
            Assert.AreEqual("Normal", preferences.WorkerPriority);
            Assert.IsFalse(preferences.CadenceDiagnosticsEnabled);
            Assert.IsFalse(preferences.AdvancedMode);
            Assert.IsFalse(preferences.KeyboardHotkeyModifiersEnabled);
            Assert.IsTrue(preferences.CrashRecoveryEnabled);
            Assert.IsNull(preferences.MainWindowPosition);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsApplicationPreferences()
    {
        var path = TemporaryPath(ConfigurationFileNames.ApplicationPreferences);
        try
        {
            ApplicationPreferencesStore.Save(path, new ApplicationPreferences
            {
                Pinned = true,
                RememberPinned = true,
                ApplyPinnedOnLaunch = false,
                CompactMode = true,
                QuickStartSeen = true,
                WorkerPriority = "AboveNormal",
                CadenceDiagnosticsEnabled = true,
                AdvancedMode = true,
                KeyboardHotkeyModifiersEnabled = true,
                CrashRecoveryEnabled = false,
                MainWindowPosition = new PersistedWindowPosition { Left = -640, Top = 120 }
            });
            var preferences = ApplicationPreferencesStore.Load(path);
            Assert.IsTrue(preferences.Pinned);
            Assert.IsTrue(preferences.RememberPinned);
            Assert.IsFalse(preferences.ApplyPinnedOnLaunch);
            Assert.IsTrue(preferences.CompactMode);
            Assert.IsTrue(preferences.QuickStartSeen);
            Assert.AreEqual("AboveNormal", preferences.WorkerPriority);
            Assert.IsTrue(preferences.CadenceDiagnosticsEnabled);
            Assert.IsTrue(preferences.AdvancedMode);
            Assert.IsTrue(preferences.KeyboardHotkeyModifiersEnabled);
            Assert.IsFalse(preferences.CrashRecoveryEnabled);
            Assert.AreEqual(-640, preferences.MainWindowPosition?.Left);
            Assert.AreEqual(120, preferences.MainWindowPosition?.Top);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void LoadWithLegacyFallback_MigratesUiPreferencesAndCrashRecoveryFromTheirOldFiles()
    {
        var currentPath = TemporaryPath(ConfigurationFileNames.ApplicationPreferences);
        var directory = Path.GetDirectoryName(currentPath)!;
        var legacyUiPath = Path.Combine(directory, ConfigurationFileNames.LegacyUiPreferences);
        var legacyRgbPath = Path.Combine(directory, ConfigurationFileNames.RgbSettings);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(legacyUiPath, "{\"Pinned\":true,\"AdvancedMode\":true}");
            File.WriteAllText(legacyRgbPath, "{\"Enabled\":true,\"CrashRecoveryEnabled\":false}");

            var preferences = ApplicationPreferencesStore.LoadWithLegacyFallback(currentPath, legacyUiPath, legacyRgbPath);

            Assert.IsTrue(preferences.Pinned);
            Assert.IsTrue(preferences.AdvancedMode);
            Assert.IsFalse(preferences.CrashRecoveryEnabled);
        }
        finally { DeleteTemporaryDirectory(currentPath); }
    }

    [TestMethod]
    public void LoadWithLegacyFallback_CurrentPreferencesTakePrecedenceOverLegacyFiles()
    {
        var currentPath = TemporaryPath(ConfigurationFileNames.ApplicationPreferences);
        var directory = Path.GetDirectoryName(currentPath)!;
        var legacyUiPath = Path.Combine(directory, ConfigurationFileNames.LegacyUiPreferences);
        var legacyRgbPath = Path.Combine(directory, ConfigurationFileNames.RgbSettings);
        try
        {
            ApplicationPreferencesStore.Save(currentPath, new ApplicationPreferences { CrashRecoveryEnabled = true });
            File.WriteAllText(legacyUiPath, "{\"Pinned\":true}");
            File.WriteAllText(legacyRgbPath, "{\"CrashRecoveryEnabled\":false}");

            var preferences = ApplicationPreferencesStore.LoadWithLegacyFallback(currentPath, legacyUiPath, legacyRgbPath);

            Assert.IsFalse(preferences.Pinned);
            Assert.IsTrue(preferences.CrashRecoveryEnabled);
        }
        finally { DeleteTemporaryDirectory(currentPath); }
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
        var path = TemporaryPath(ConfigurationFileNames.ApplicationPreferences);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not json");
            var preferences = ApplicationPreferencesStore.Load(path);
            Assert.IsFalse(preferences.Pinned);
            Assert.IsTrue(preferences.RememberPinned);
            Assert.IsTrue(preferences.ApplyPinnedOnLaunch);
            Assert.IsTrue(preferences.CrashRecoveryEnabled);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void Load_LegacyUiFileKeepsExistingPinnedBehavior()
    {
        var path = TemporaryPath(ConfigurationFileNames.LegacyUiPreferences);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"Pinned\":true}");

            var preferences = ApplicationPreferencesStore.Load(path);

            Assert.IsTrue(preferences.Pinned);
            Assert.IsTrue(preferences.RememberPinned);
            Assert.IsTrue(preferences.ApplyPinnedOnLaunch);
            Assert.IsTrue(PinnedWindowPreferenceRules.ApplyOnLaunch(preferences));
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void PinnedRules_DistinguishImmediateDeferredAndForgottenState()
    {
        var deferred = new ApplicationPreferences { Pinned = true, RememberPinned = true, ApplyPinnedOnLaunch = false };
        Assert.IsFalse(PinnedWindowPreferenceRules.ApplyOnLaunch(deferred));
        Assert.IsTrue(PinnedWindowPreferenceRules.DeferUntilInteraction(deferred));

        var forgotten = new ApplicationPreferences { Pinned = true, RememberPinned = false, ApplyPinnedOnLaunch = true };
        Assert.IsFalse(PinnedWindowPreferenceRules.ApplyOnLaunch(forgotten));
        Assert.IsFalse(PinnedWindowPreferenceRules.DeferUntilInteraction(forgotten));
        Assert.IsFalse(PinnedWindowPreferenceRules.PersistedPinnedState(false, currentPinnedState: true));
        Assert.IsTrue(PinnedWindowPreferenceRules.PersistedPinnedState(true, currentPinnedState: true));
    }

    private static string TemporaryPath(string fileName) => Path.Combine(Path.GetTempPath(), TestDirectoryName, Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat), fileName);
    private static void DeleteTemporaryDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
