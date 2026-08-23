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
            app.SelectInput("Left click");
            app.SelectActionType("While held");
            app.SetIntervalMilliseconds(70);
            Assert.IsFalse(app.FiniteRepeatEnabled, "While held must not leave a finite repeat that can restart on key repeat");
            app.SaveAsDefault();

            using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
            {
                session.WaitFor(() => !app.StartEnabled && app.StopEnabled, "held simple hotkey did not start");
                Thread.Sleep(360);
                Assert.IsTrue(InputEventCount(fixture) >= 8, "the action did not repeat at the configured interval while held");
            }

            app.WaitUntilStopped();
            AssertInputRemainsStopped(fixture);
        }

        var saved = fixture.ReadSimpleDefaults();
        Assert.AreEqual("While held", saved.ClickType);
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
        AutomationProfileStore.Save(fixture.TestFile("automation-profiles.json"), document);

        using (var session = fixture.Launch(registerKeyboardHotkeys: true))
        {
            var app = new MainWindowRobot(session);
            session.Editor.Select(EditorScope.Hotkey);
            app.SelectActionType("While held");
            session.Editor.SaveProfile();

            using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
            {
                session.WaitFor(
                    () => session.MainElement($"StopAction_{ProfileE2EFixture.ActionId}").AsButton().IsEnabled,
                    "held profile hotkey did not start");
                Thread.Sleep(430);
                Assert.IsTrue(InputEventCount(fixture) >= 8,
                    "the held action did not use the inherited 80 ms profile interval");
            }

            session.WaitFor(
                () => session.MainElement($"StartAction_{ProfileE2EFixture.ActionId}").AsButton().IsEnabled,
                "releasing the profile hotkey did not stop its run");
            AssertInputRemainsStopped(fixture);
        }

        var stored = fixture.ReadProfiles().Profiles.Single().Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);
        Assert.AreEqual("While held", stored.Settings.ClickType);
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
        app.SelectActionType("While held");

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

    private static void AssertInputRemainsStopped(ProfileE2EFixture fixture)
    {
        var stoppedCount = InputEventCount(fixture);
        Thread.Sleep(220);
        Assert.AreEqual(stoppedCount, InputEventCount(fixture), "input continued after the held hotkey was released");
    }
}
