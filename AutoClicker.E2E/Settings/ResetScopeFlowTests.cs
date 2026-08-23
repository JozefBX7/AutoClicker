// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class ResetScopeFlowTests
{
    [TestMethod]
    public void ResetSimpleMode_RestoresSimpleDefaultsWithoutRemovingProfiles()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.SetIntervalMilliseconds(999);
        app.SaveAsDefault();
        app.OpenSettings();
        new SettingsRobot(session).Reset("Reset Simple mode");

        session.WaitFor(() => fixture.ReadSimpleDefaults().Milliseconds == 100, "Simple reset did not persist");
        Assert.AreEqual(100, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.IsTrue(fixture.ReadProfiles().Profiles.Any(profile => profile.Id == ProfileE2EFixture.ProfileId));
    }

    [TestMethod]
    public void ResetAdvancedMode_ReplacesProfilesButKeepsAdvancedSharedDefaults()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        new SettingsRobot(session).Reset("Reset Advanced mode");

        session.WaitFor(() => fixture.ReadProfiles().Profiles.SingleOrDefault()?.Name == "General", "Advanced profile reset did not persist");
        Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds, fixture.ReadGlobalDefaults().Milliseconds);
        Assert.AreEqual("General", fixture.ReadProfiles().Profiles.Single().Name);
    }

    [TestMethod]
    public void ResetAdvancedSharedDefaults_KeepsProfilesAndRestoresGlobalValues()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        session.Editor.Select(EditorScope.Global);
        session.Editor.EnterMilliseconds(888);
        Assert.AreEqual(888, fixture.ReadGlobalDefaults().Milliseconds);

        var app = new MainWindowRobot(session);
        app.OpenSettings();
        new SettingsRobot(session).Reset("Reset Advanced shared defaults");

        session.WaitFor(() => fixture.ReadGlobalDefaults().Milliseconds == 100, "Advanced shared reset did not persist");
        Assert.IsTrue(fixture.ReadProfiles().Profiles.Any(profile => profile.Id == ProfileE2EFixture.ProfileId));
    }

    [TestMethod]
    public void ResetEverything_RestoresFactoryModePreferencesProfilesAndDefaults()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.TogglePin();
        app.CollapseSettings();
        app.OpenSettings();
        new SettingsRobot(session).Reset("Reset everything");

        session.WaitFor(() => !fixture.ReadApplicationPreferences().AdvancedMode, "factory reset did not persist application preferences");
        var preferences = fixture.ReadApplicationPreferences();
        Assert.IsFalse(preferences.AdvancedMode);
        Assert.IsFalse(preferences.Pinned);
        Assert.IsFalse(preferences.CompactMode);
        Assert.AreEqual(100, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.AreEqual(100, fixture.ReadGlobalDefaults().Milliseconds);
        Assert.AreEqual("General", fixture.ReadProfiles().Profiles.Single().Name);
        Assert.IsTrue(preferences.CrashRecoveryEnabled);
    }
}
