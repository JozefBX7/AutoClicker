// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class BackupAndProfileTransferFlowTests
{
    [DataTestMethod]
    [DataRow("Everything", (int)BackupScope.Everything)]
    [DataRow("Simple", (int)BackupScope.SimpleMode)]
    [DataRow("Advanced", (int)BackupScope.AdvancedMode)]
    [DataRow("Sequences", (int)BackupScope.CustomSequences)]
    public void EveryBackupScope_ExportsTheExpectedVersionedDocument(string scopeName, int expectedScopeValue)
    {
        using var fixture = new ProfileE2EFixture();
        fixture.WriteSequenceLibrary([Sequence($"{scopeName} sequence")]);
        var path = fixture.TestFile($"{scopeName}.backup.json");
        using var session = fixture.Launch(saveFile: path);
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);
        var openRgbStatus = settings.OpenRgbStatus;
        var scope = (BackupScope)expectedScopeValue;
        settings.Export(scopeName);

        session.WaitFor(() => File.Exists(path), $"{scopeName} backup was not created");
        session.WaitFor(() => settings.BackupStatus == $"{BackupScopeInfo.DisplayName(scope)} exported.",
            $"{scopeName} export feedback did not appear in the backup section");
        Assert.AreEqual(openRgbStatus, settings.OpenRgbStatus,
            $"{scopeName} export feedback unexpectedly replaced the OpenRGB status");
        var backup = ConfigBackupStore.Read(path);
        Assert.AreEqual(scope, backup.Scope);
        Assert.IsTrue(backup.SchemaVersion > 0);
        AssertBackupPartition(backup, scope, scopeName);
    }

    [TestMethod]
    public void EverythingBackup_RestoresModePreferencesDefaultsAndProfilesAfterFactoryReset()
    {
        using var fixture = new ProfileE2EFixture();
        var path = fixture.TestFile("everything-roundtrip.backup.json");
        using var session = fixture.Launch(saveFile: path, openFile: path);
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);
        settings.Export("Everything");
        session.WaitFor(() => File.Exists(path), "Everything backup was not created");
        settings.Reset("Reset everything");
        session.WaitFor(() => fixture.ReadProfiles().Profiles.Single().Name == "General", "factory reset did not complete");

        app.OpenSettings();
        settings = new SettingsRobot(session);
        settings.Restore("Everything");
        session.WaitFor(() => fixture.ReadApplicationPreferences().AdvancedMode
                && fixture.ReadProfiles().Profiles.Any(profile => profile.Id == ProfileE2EFixture.ProfileId),
            "Everything backup did not finish restoring preferences and profiles");

        Assert.IsTrue(fixture.ReadApplicationPreferences().AdvancedMode);
        Assert.AreEqual(50, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds, fixture.ReadGlobalDefaults().Milliseconds);
        Assert.AreEqual(2, fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId).Actions.Count);
    }

    [TestMethod]
    public void EverythingBackup_CapturesUnsavedSettingsAndReopensWithTheRestoredValues()
    {
        using var fixture = new ProfileE2EFixture();
        var path = fixture.TestFile("everything-visible-settings.backup.json");
        using var session = fixture.Launch(saveFile: path, openFile: path);
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);
        settings.SelectMode("Simple");
        settings.SelectWorkerPriority("Above Normal (compatibility)");
        settings.SetKeyboardModifiers(true);
        settings.SetCadenceDiagnostics(true);
        settings.SetCrashRecovery(true);
        settings.SetOpenRgb(true);
        settings.SetOpenRgbAutoStart(true);
        settings.SetStopAutoStartedOpenRgb(false);
        settings.SetIndicatorColor("#123abc");
        settings.Export("Everything");

        session.WaitFor(() => File.Exists(path), "Everything backup was not created");
        var backup = ConfigBackupStore.Read(path);
        var exportedPreferences = JsonSerializer.Deserialize<ApplicationPreferences>(backup.ApplicationPreferencesJson);
        var exportedRgb = JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson);
        Assert.IsNotNull(exportedPreferences);
        Assert.IsFalse(exportedPreferences.AdvancedMode);
        Assert.AreEqual(WorkerPriorityOption.AboveNormal.ToString(), exportedPreferences.WorkerPriority);
        Assert.IsTrue(exportedPreferences.KeyboardHotkeyModifiersEnabled);
        Assert.IsTrue(exportedPreferences.CadenceDiagnosticsEnabled);
        Assert.IsTrue(exportedPreferences.CrashRecoveryEnabled);
        Assert.IsNotNull(exportedRgb);
        Assert.IsTrue(exportedRgb.Enabled);
        Assert.IsTrue(exportedRgb.AutoStart);
        Assert.IsFalse(exportedRgb.StopAutoStartedOnExit);
        Assert.AreEqual("#123ABC", exportedRgb.IndicatorColor);

        // Exporting is a snapshot, not an implicit Save.
        settings.Cancel();
        Assert.IsTrue(fixture.ReadApplicationPreferences().AdvancedMode);
        Assert.IsFalse(fixture.ReadRgbSettings().Enabled);

        app.OpenSettings();
        new SettingsRobot(session).Restore("Everything");
        session.WaitFor(() => !fixture.ReadApplicationPreferences().AdvancedMode,
            "Everything restore did not persist the exported Settings values");

        // A complete restore closes the stale dialog. Reopening it must show the restored snapshot,
        // and saving that fresh view must not undo the restore.
        app.OpenSettings();
        new SettingsRobot(session).Save();
        var restoredPreferences = fixture.ReadApplicationPreferences();
        var restoredRgb = fixture.ReadRgbSettings();
        Assert.IsFalse(restoredPreferences.AdvancedMode);
        Assert.AreEqual(WorkerPriorityOption.AboveNormal.ToString(), restoredPreferences.WorkerPriority);
        Assert.IsTrue(restoredPreferences.KeyboardHotkeyModifiersEnabled);
        Assert.IsTrue(restoredPreferences.CadenceDiagnosticsEnabled);
        Assert.IsTrue(restoredPreferences.CrashRecoveryEnabled);
        Assert.IsTrue(restoredRgb.Enabled);
        Assert.IsTrue(restoredRgb.AutoStart);
        Assert.IsFalse(restoredRgb.StopAutoStartedOnExit);
        Assert.AreEqual("#123ABC", restoredRgb.IndicatorColor);
    }

    [TestMethod]
    public void SequenceBackup_RestoresADeletedSequenceLibrary()
    {
        using var fixture = new ProfileE2EFixture();
        fixture.WriteSequenceLibrary([Sequence("Restorable sequence")]);
        var path = fixture.TestFile("sequences-roundtrip.backup.json");
        using var session = fixture.Launch(saveFile: path, openFile: path);
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);
        settings.Export("Sequences");
        session.WaitFor(() => File.Exists(path), "sequence backup was not created");
        settings.Cancel();

        fixture.WriteSequenceLibrary([]);
        Assert.AreEqual(0, fixture.ReadSequenceLibrary().Count);
        app.OpenSettings();
        settings = new SettingsRobot(session);
        var openRgbStatus = settings.OpenRgbStatus;
        settings.Restore("Sequences");
        session.WaitFor(() => fixture.ReadSequenceLibrary().Count == 1, "sequence library was not restored");
        session.WaitFor(() => settings.BackupStatus == "Custom sequences restored. Close Settings to use it.",
            "restore feedback did not appear in the backup section");
        Assert.AreEqual(openRgbStatus, settings.OpenRgbStatus,
            "restore feedback unexpectedly replaced the OpenRGB status");
        settings.Cancel();
        Assert.AreEqual("Restorable sequence", fixture.ReadSequenceLibrary().Single().Name);
    }

    [TestMethod]
    public void ProfileExportAndImport_RoundTripsWithFreshProfileAndActionIds()
    {
        using var fixture = new ProfileE2EFixture();
        var seededDocument = fixture.ReadProfiles();
        var seededAction = seededDocument.Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId)
            .Actions.Single(action => action.Id == ProfileE2EFixture.ActionId);
        seededAction.HotkeyEnabled = false;
        seededAction.EnableToggleHotkey = new AutomationHotkeyBinding
        {
            VirtualKey = 120,
            Modifiers = 0x2u | 0x4u,
            Trigger = HotkeyTrigger.Keyboard
        };
        AutomationProfileStore.Save(fixture.TestFile(ConfigurationFileNames.AutomationProfiles), seededDocument);

        var path = fixture.TestFile("profile.autoclicker-profile.json");
        using var session = fixture.Launch(saveFile: path, openFile: path);
        var profiles = new ProfileOptionsRobot(session);
        profiles.ChooseMenu(ProfileE2EFixture.ProfileId, "ExportProfile");
        session.WaitFor(() => File.Exists(path), "profile export was not created");

        session.MainElement("ManageProfiles").AsButton().Invoke();
        session.DesktopElement("ImportProfile").AsMenuItem().Invoke();
        session.WaitFor(() => fixture.ReadProfiles().Profiles.Count == 2, "profile import did not persist");

        var document = fixture.ReadProfiles();
        var original = document.Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
        var imported = document.Profiles.Single(profile => profile.Id != ProfileE2EFixture.ProfileId);
        Assert.AreNotEqual(original.Id, imported.Id);
        Assert.IsFalse(original.Actions.Select(action => action.Id).Intersect(imported.Actions.Select(action => action.Id)).Any());
        Assert.AreEqual(original.Actions.Count, imported.Actions.Count);

        var originalToggleAction = original.Actions.Single(action => action.Id == ProfileE2EFixture.ActionId);
        var importedToggleAction = imported.Actions.Single(action => action.Settings.Hotkey == originalToggleAction.Settings.Hotkey);
        Assert.IsFalse(importedToggleAction.HotkeyEnabled,
            "profile import did not preserve the action's disabled state");
        Assert.IsNotNull(importedToggleAction.EnableToggleHotkey,
            "profile import did not preserve the action's enable-toggle binding");
        Assert.AreEqual(originalToggleAction.EnableToggleHotkey!.VirtualKey, importedToggleAction.EnableToggleHotkey!.VirtualKey);
        Assert.AreEqual(originalToggleAction.EnableToggleHotkey.Modifiers, importedToggleAction.EnableToggleHotkey.Modifiers);
        Assert.AreEqual(originalToggleAction.EnableToggleHotkey.Trigger, importedToggleAction.EnableToggleHotkey.Trigger);
    }

    private static void AssertBackupPartition(ConfigBackupDocument backup, BackupScope scope, string scopeName)
    {
        var includesSimple = scope is BackupScope.Everything or BackupScope.SimpleMode;
        var includesAdvanced = scope is BackupScope.Everything or BackupScope.AdvancedMode;
        var includesSequences = scope is BackupScope.Everything or BackupScope.CustomSequences;
        var includesAppSettings = scope == BackupScope.Everything;

        AssertSection(backup.LegacySharedDefaultsJson, includesSimple, scopeName, "legacy-compatible Simple defaults");
        AssertSection(backup.SimpleDefaultsJson, includesSimple, scopeName, "Simple defaults");
        AssertSection(backup.AdvancedDefaultsJson, includesAdvanced, scopeName, "Advanced defaults");
        AssertSection(backup.AutomationProfilesJson, includesAdvanced, scopeName, "Advanced profiles");
        AssertSection(backup.SequenceLibraryJson, includesSequences, scopeName, "custom sequences");
        AssertSection(backup.RgbJson, includesAppSettings, scopeName, "RGB settings");
        AssertSection(backup.ApplicationPreferencesJson, includesAppSettings, scopeName, "application preferences");
        AssertSection(backup.AppearanceJson, includesAppSettings, scopeName, "appearance settings");
        Assert.IsTrue(string.IsNullOrWhiteSpace(backup.LegacyApplicationPreferencesJson),
            $"{scopeName} unexpectedly populated the legacy application-preferences field in a current-schema backup");

        if (includesSimple)
            Assert.AreEqual(50, JsonSerializer.Deserialize<AppDefaults>(backup.SimpleDefaultsJson)?.Milliseconds,
                $"{scopeName} did not export the seeded Simple defaults payload");
        if (includesAdvanced)
        {
            Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds,
                JsonSerializer.Deserialize<AppDefaults>(backup.AdvancedDefaultsJson)?.Milliseconds,
                $"{scopeName} did not export the seeded Advanced defaults payload");
            var profiles = JsonSerializer.Deserialize<AutomationProfileDocument>(backup.AutomationProfilesJson);
            Assert.IsTrue(profiles?.Profiles.Any(profile => profile.Id == ProfileE2EFixture.ProfileId) == true,
                $"{scopeName} did not export the seeded Advanced profile payload");
        }
        if (includesSequences)
            Assert.AreEqual($"{scopeName} sequence", SequenceLibraryStore.Deserialize(backup.SequenceLibraryJson).Single().Name,
                $"{scopeName} did not export the seeded custom-sequence payload");
        if (includesAppSettings)
        {
            Assert.IsTrue(JsonSerializer.Deserialize<ApplicationPreferences>(backup.ApplicationPreferencesJson)?.AdvancedMode == true,
                "Everything did not export the seeded application preferences payload");
            Assert.IsFalse(JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson)?.AutoStart ?? true,
                "Everything did not export the seeded RGB payload");
        }
    }

    private static void AssertSection(string json, bool expected, string scopeName, string sectionName) =>
        Assert.AreEqual(expected, !string.IsNullOrWhiteSpace(json),
            $"{scopeName} backup {(expected ? "omitted" : "unexpectedly included")} {sectionName}");

    private static SequencePreset Sequence(string name) => new()
    {
        Name = name,
        Steps =
        [
            new SequenceStep { Input = AutomationInputIds.Left },
            new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x20 }
        ]
    };
}
