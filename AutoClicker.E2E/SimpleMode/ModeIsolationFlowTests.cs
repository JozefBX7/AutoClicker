// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class ModeIsolationFlowTests
{
    [TestMethod]
    public void SavedSimpleAndAdvancedConfigurations_RemainIndependentAcrossModeRoundTripsAndRestart()
    {
        using var fixture = new ProfileE2EFixture();
        using (var session = fixture.Launch())
        {
            var app = new MainWindowRobot(session);
            session.Editor.Select(EditorScope.Global);
            session.Editor.EnterMilliseconds(111);
            session.Editor.Select(EditorScope.Profile);
            session.Editor.EnterMilliseconds(222);
            session.Editor.SaveProfile();
            session.Editor.Select(EditorScope.Hotkey);
            session.Editor.EnterMilliseconds(333);
            session.Editor.SaveProfile();

            app.SwitchMode();
            Assert.AreEqual(50, session.Editor.Milliseconds, "Simple mode loaded an Advanced interval");
            app.SetIntervalMilliseconds(444);
            app.SelectInput("Right click");
            app.SelectActionType("Double");
            app.SetFiniteRepeat(9);
            app.SetFixedPosition(-44, 55);
            app.DisableTargetWindow();
            app.CaptureHotkey(VirtualKeyShort.F8);
            app.SaveAsDefault();

            app.SwitchMode();
            AssertAdvancedIntervals(session, 111, 222, 333);

            app.SwitchMode();
            Assert.AreEqual(444, session.Editor.Milliseconds);
            Assert.AreEqual("Right click", app.SelectedInput);
            Assert.AreEqual(9, session.Editor.RepeatCount);
            Assert.AreEqual(-44, session.Editor.CursorX);
            Assert.AreEqual(55, session.Editor.CursorY);
            Assert.IsFalse(session.Editor.TargetWindowEnabled);
        }

        AssertStoredModes(fixture, simple: 444, global: 111, profile: 222, hotkey: 333);

        using var restarted = fixture.Launch();
        var restartedApp = new MainWindowRobot(restarted);
        Assert.AreEqual(444, restarted.Editor.Milliseconds);
        Assert.AreEqual("Right click", restartedApp.SelectedInput);
        Assert.AreEqual("F8", restarted.MainElement("HotkeyLabel").Name);
        restartedApp.SwitchMode();
        AssertAdvancedIntervals(restarted, 111, 222, 333);
    }

    [TestMethod]
    public void UnsavedAdvancedProfileEdit_SurvivesTemporarySimpleUse_ThenDiscardLeavesSimpleDefaultsUntouched()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);

        session.Editor.Select(EditorScope.Profile);
        session.Editor.EnterMilliseconds(876);
        app.SwitchMode();
        app.SetIntervalMilliseconds(654);
        app.SaveAsDefault();

        app.SwitchMode();
        session.Editor.Select(EditorScope.Profile);
        Assert.AreEqual(876, session.Editor.Milliseconds,
            "the unsaved Advanced edit was lost during temporary Simple-mode use");
        session.Editor.DiscardSelectedProfileChanges();
        Assert.AreEqual(ProfileE2EFixture.ProfileMilliseconds, session.Editor.Milliseconds);

        app.SwitchMode();
        Assert.AreEqual(654, session.Editor.Milliseconds,
            "discarding the Advanced profile unexpectedly changed Simple defaults");
        Assert.AreEqual(654, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.AreEqual(
            ProfileE2EFixture.ProfileMilliseconds,
            fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId)
                .BehaviorDefaults!.Milliseconds);
    }

    private static void AssertAdvancedIntervals(
        AutoClickerE2ESession session,
        int global,
        int profile,
        int hotkey)
    {
        session.Editor.Select(EditorScope.Global);
        Assert.AreEqual(global, session.Editor.Milliseconds);
        session.Editor.Select(EditorScope.Profile);
        Assert.AreEqual(profile, session.Editor.Milliseconds);
        session.Editor.Select(EditorScope.Hotkey);
        Assert.AreEqual(hotkey, session.Editor.Milliseconds);
    }

    private static void AssertStoredModes(
        ProfileE2EFixture fixture,
        int simple,
        int global,
        int profile,
        int hotkey)
    {
        Assert.AreEqual(simple, fixture.ReadSimpleDefaults().Milliseconds);
        Assert.AreEqual(global, fixture.ReadGlobalDefaults().Milliseconds);
        var storedProfile = fixture.ReadProfiles().Profiles.Single(item => item.Id == ProfileE2EFixture.ProfileId);
        Assert.AreEqual(profile, storedProfile.BehaviorDefaults!.Milliseconds);
        Assert.AreEqual(hotkey, storedProfile.Actions.Single(action => action.Id == ProfileE2EFixture.ActionId).Settings.Milliseconds);
    }
}
