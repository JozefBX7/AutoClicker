// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class WhileHeldHotkeyFlowTests
{
    [TestMethod]
    public void SimpleKeyboardHotkey_RunsOnlyWhileHeldAndPersistsTheMode()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false, chordedKeyboardHotkeys: true);
        using (var session = fixture.Launch(registerKeyboardHotkeys: true))
        {
            var app = new MainWindowRobot(session);
            app.DisableTargetWindow();
            app.SelectInput(AutomationInputLabels.LeftClick);
            app.SelectActionType(AutomationActionTypeIds.WhileHeld);
            app.SetIntervalMilliseconds(70);
            Assert.IsFalse(app.FiniteRepeatEnabled, "While held must not leave a finite repeat that can restart on key repeat");
            app.SaveAsDefault();

            using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
            {
                session.WaitFor(() => !app.StartEnabled && app.StopEnabled, "held simple hotkey did not start");
                var countBeforeSample = InputEventCount(fixture);
                Thread.Sleep(360);
                AssertInputCountWithin(fixture, countBeforeSample, minimum: 8, maximum: 16,
                    "the simple action did not maintain a bounded cadence near its configured 70 ms interval");
            }

            app.WaitUntilStopped();
            AssertInputRemainsStopped(fixture);
        }

        var saved = fixture.ReadSimpleDefaults();
        Assert.AreEqual(AutomationActionTypeIds.WhileHeld, saved.ClickType);
        Assert.AreEqual(70, saved.Milliseconds);
    }

    [TestMethod]
    public void AdvancedKeyboardHotkey_UsesInheritedProfileIntervalAndCancelsTheExactRunOnRelease()
    {
        using var fixture = new ProfileE2EFixture(chordedKeyboardHotkeys: true);
        var document = fixture.ReadProfiles();
        var profile = document.Profiles.Single(item => item.Id == ProfileE2EFixture.ProfileId);
        var action = profile.Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);
        profile.BehaviorDefaults!.Milliseconds = 80;
        action.BehaviorOverrides &= ~AutomationBehaviorOverride.Interval;
        action.Settings.TargetExecutable = string.Empty;
        action.Settings.TargetWindowEnabled = false;
        AutomationProfileStore.Save(fixture.TestFile(ConfigurationFileNames.AutomationProfiles), document);

        using (var session = fixture.Launch(registerKeyboardHotkeys: true))
        {
            var app = new MainWindowRobot(session);
            session.Editor.Select(EditorScope.Hotkey);
            app.SelectActionType(AutomationActionTypeIds.WhileHeld);
            session.Editor.SaveProfile();

            using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
            {
                session.WaitFor(
                    () => session.MainElement($"StopAction_{ProfileE2EFixture.ActionId}").AsButton().IsEnabled,
                    "held profile hotkey did not start");
                var countBeforeSample = InputEventCount(fixture);
                Thread.Sleep(430);
                AssertInputCountWithin(fixture, countBeforeSample, minimum: 8, maximum: 16,
                    "the held action did not maintain a bounded cadence near its inherited 80 ms profile interval");
            }

            session.WaitFor(
                () => session.MainElement($"StartAction_{ProfileE2EFixture.ActionId}").AsButton().IsEnabled,
                "releasing the profile hotkey did not stop its run");
            AssertInputRemainsStopped(fixture);
        }

        var stored = fixture.ReadProfiles().Profiles.Single().Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);
        Assert.AreEqual(AutomationActionTypeIds.WhileHeld, stored.Settings.ClickType);
        Assert.IsFalse(stored.ActiveBehaviorOverrides.HasFlag(AutomationBehaviorOverride.Interval),
            "choosing While held must not turn an inherited interval into a local override");
    }

    [TestMethod]
    public void WhileHeldKeyboardHotkey_CannotRunAnActionThatEmitsItsOwnTriggerKey()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false, chordedKeyboardHotkeys: true);
        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectCustomKey(VirtualKeyShort.F6);
        app.SelectActionType(AutomationActionTypeIds.WhileHeld);

        using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
            session.WaitFor(
                () => app.Status.Contains("cannot send its own hotkey key", StringComparison.OrdinalIgnoreCase),
                "self-triggering held action was not rejected");

        Assert.IsTrue(app.StartEnabled);
        Assert.IsFalse(app.StopEnabled);
        Assert.AreEqual(0, InputEventCount(fixture), "a rejected self-triggering action must not start a worker or emit input");
    }

    private static int InputEventCount(ProfileE2EFixture fixture) =>
        fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal));

    private static void AssertInputCountWithin(
        ProfileE2EFixture fixture,
        int countBeforeSample,
        int minimum,
        int maximum,
        string message)
    {
        var sampledCount = InputEventCount(fixture) - countBeforeSample;
        Assert.IsTrue(sampledCount >= minimum && sampledCount <= maximum,
            $"{message}. Expected {minimum}–{maximum} safe input events but observed {sampledCount}.");
    }

    private static void AssertInputRemainsStopped(ProfileE2EFixture fixture)
    {
        var stoppedCount = InputEventCount(fixture);
        Thread.Sleep(220);
        Assert.AreEqual(stoppedCount, InputEventCount(fixture), "input continued after the held hotkey was released");
    }
}
