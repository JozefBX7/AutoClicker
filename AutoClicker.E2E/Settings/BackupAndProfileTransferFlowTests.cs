// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        if (scopeName == "Sequences") fixture.WriteSequenceLibrary([Sequence("Exported sequence")]);
        var path = fixture.TestFile($"{scopeName}.backup.json");
        using var session = fixture.Launch(saveFile: path);
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        new SettingsRobot(session).Export(scopeName);

        session.WaitFor(() => File.Exists(path), $"{scopeName} backup was not created");
        var backup = ConfigBackupStore.Read(path);
        Assert.AreEqual((BackupScope)expectedScopeValue, backup.Scope);
        Assert.IsTrue(backup.SchemaVersion > 0);
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
        session.WaitFor(() => fixture.ReadProfiles().Profiles.Any(profile => profile.Id == ProfileE2EFixture.ProfileId),
            "Everything backup did not restore profiles");
        settings.Cancel();

        Assert.IsTrue(fixture.ReadUiPreferences().AdvancedMode);
        Assert.AreEqual(50, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds, fixture.ReadGlobalDefaults().Milliseconds);
        Assert.AreEqual(2, fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId).Actions.Count);
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
        settings.Restore("Sequences");
        session.WaitFor(() => fixture.ReadSequenceLibrary().Count == 1, "sequence library was not restored");
        settings.Cancel();
        Assert.AreEqual("Restorable sequence", fixture.ReadSequenceLibrary().Single().Name);
    }

    [TestMethod]
    public void ProfileExportAndImport_RoundTripsWithFreshProfileAndActionIds()
    {
        using var fixture = new ProfileE2EFixture();
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
    }

    private static SequencePreset Sequence(string name) => new()
    {
        Name = name,
        Steps =
        [
            new SequenceStep { Input = "Left" },
            new SequenceStep { Input = "Custom", CustomKey = 0x20 }
        ]
    };
}
