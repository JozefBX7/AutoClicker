// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class EnableToggleHotkeyFlowTests
{
    [TestMethod]
    public void EnableToggleBinding_RejectsConflictsStopsTheRunAndRestartsHeldModeImmediately()
    {
        using var fixture = new ProfileE2EFixture(chordedKeyboardHotkeys: true);
        var document = fixture.ReadProfiles();
        var action = document.Profiles.Single().Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);
        action.Settings.ClickType = AutomationActionTypeIds.WhileHeld;
        action.Settings.TargetExecutable = string.Empty;
        action.Settings.TargetWindowEnabled = false;
        AutomationProfileStore.Save(fixture.TestFile(ConfigurationFileNames.AutomationProfiles), document);

        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var actions = new AdvancedActionRobot(session);
        var profiles = new ProfileOptionsRobot(session);
        var app = new MainWindowRobot(session);

        actions.ConfigureEnableToggleHotkeyChord(ProfileE2EFixture.ActionId, VirtualKeyShort.F7);
        session.WaitFor(
            () => app.AdvancedStatus.Contains("already assigned", StringComparison.OrdinalIgnoreCase),
            "an enable-toggle binding was allowed to overlap another action hotkey");

        actions.ConfigureEnableToggleHotkeyChord(ProfileE2EFixture.ActionId, VirtualKeyShort.F9);
        profiles.SaveExisting();
        var configured = StoredAction(fixture);
        Assert.AreEqual(120, configured.EnableToggleHotkey?.VirtualKey);
        Assert.AreEqual(0x2u | 0x4u, configured.EnableToggleHotkey?.Modifiers);

        using (session.HoldRegisteredKeyboardHotkey(VirtualKeyShort.F6))
        {
            session.WaitFor(() => StopButton(session).IsEnabled, "the While-held action did not start");
            session.WaitFor(() => InputEventCount(fixture) > 0, "the enabled While-held action did not reach the safe input journal");

            using (Keyboard.Pressing(VirtualKeyShort.F9))
            {
                session.WaitFor(() => !StopButton(session).IsEnabled, "the enable-toggle binding did not stop the running action");
                var countWhenDisabled = InputEventCount(fixture);
                Thread.Sleep(400);
                Assert.AreEqual(countWhenDisabled, InputEventCount(fixture),
                    "input continued after the enable-toggle binding disabled the running action");
            }

            profiles.SaveExisting();
            Assert.IsFalse(StoredAction(fixture).HotkeyEnabled, "holding the enable-toggle key repeated and changed state more than once");

            Keyboard.Press(VirtualKeyShort.F9);
            session.WaitFor(
                () => StopButton(session).IsEnabled,
                "re-enabling a While-held action did not start it while its run hotkey was already down");
            var countBeforeRestart = InputEventCount(fixture);
            session.WaitFor(() => InputEventCount(fixture) > countBeforeRestart,
                "re-enabling the action while its run hotkey was held did not resume safe journal input");
        }

        session.WaitFor(() => !StopButton(session).IsEnabled, "releasing the run hotkey did not stop the re-enabled action");
        var countAfterRelease = InputEventCount(fixture);
        Thread.Sleep(400);
        Assert.AreEqual(countAfterRelease, InputEventCount(fixture),
            "input continued after releasing the run hotkey from the re-enabled action");
        profiles.SaveExisting();
        Assert.IsTrue(StoredAction(fixture).HotkeyEnabled);
        Assert.AreEqual(120, StoredAction(fixture).EnableToggleHotkey?.VirtualKey);
    }

    private static FlaUI.Core.AutomationElements.Button StopButton(AutoClickerE2ESession session) =>
        session.MainElement($"StopAction_{ProfileE2EFixture.ActionId}").AsButton();

    private static AutomationAction StoredAction(ProfileE2EFixture fixture) =>
        fixture.ReadProfiles().Profiles.Single().Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);

    private static int InputEventCount(ProfileE2EFixture fixture) =>
        fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal));
}
