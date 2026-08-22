// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class HotkeyToggleFlowTests
{
    [TestMethod]
    public void SimpleRegisteredKeyboardChord_TogglesMacroOnAndOffThroughWindows()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false, chordedKeyboardHotkeys: true);
        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SetRepeatUntilStopped();
        AssertRegistered(session, fixture, VirtualKeyShort.F6, "primary");

        session.SendRegisteredKeyboardHotkey(VirtualKeyShort.F6);
        session.WaitFor(() => !app.StartEnabled && app.StopEnabled, "simple hotkey did not toggle automation on");
        session.WaitFor(() => fixture.ReadRuntimeEvents().Any(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "the toggled simple macro did not execute");

        session.SendRegisteredKeyboardHotkey(VirtualKeyShort.F6);
        app.WaitUntilStopped();
    }

    [TestMethod]
    public void AdvancedRegisteredKeyboardChords_FollowSelectedAndAdditionalActionsAfterReregistration()
    {
        using var fixture = new ProfileE2EFixture(chordedKeyboardHotkeys: true);
        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var app = new MainWindowRobot(session);
        session.Editor.Select(EditorScope.Hotkey);
        app.DisableTargetWindow();
        app.SetRepeatUntilStopped();
        AssertRegistered(session, fixture, VirtualKeyShort.F7, ProfileE2EFixture.SecondActionId);

        ToggleAndAssert(session, fixture, VirtualKeyShort.F7, ProfileE2EFixture.SecondActionId);

        session.MainElement($"Action_{ProfileE2EFixture.SecondActionId}").AsButton().Invoke();
        session.WaitFor(() => session.Editor.Milliseconds == 75,
            "the second action did not become active before testing re-registration");
        AssertRegistered(session, fixture, VirtualKeyShort.F7, "primary");
        AssertRegistered(session, fixture, VirtualKeyShort.F6, ProfileE2EFixture.ActionId);

        ToggleAndAssert(session, fixture, VirtualKeyShort.F7, ProfileE2EFixture.SecondActionId);
        ToggleAndAssert(session, fixture, VirtualKeyShort.F6, ProfileE2EFixture.ActionId);
    }

    private static void ToggleAndAssert(
        AutoClickerE2ESession session,
        ProfileE2EFixture fixture,
        VirtualKeyShort key,
        string actionId)
    {
        var eventCount = fixture.ReadRuntimeEvents().Count;
        session.SendRegisteredKeyboardHotkey(key);
        session.WaitFor(
            () => session.MainElement($"StopAction_{actionId}").AsButton().IsEnabled,
            $"registered {key} did not toggle action '{actionId}' on");
        session.WaitFor(() => fixture.ReadRuntimeEvents().Count > eventCount,
            $"action '{actionId}' did not execute through the safe input sink");

        session.SendRegisteredKeyboardHotkey(key);
        session.WaitFor(
            () => session.MainElement($"StartAction_{actionId}").AsButton().IsEnabled,
            $"registered {key} did not toggle action '{actionId}' off");
    }

    private static void AssertRegistered(
        AutoClickerE2ESession session,
        ProfileE2EFixture fixture,
        VirtualKeyShort key,
        string action)
    {
        var signature = $"key={(int)key};modifiers=6;action={action};success=True";
        session.WaitFor(
            () => fixture.ReadRuntimeEvents().Any(line =>
                line.Contains("\thotkey-registration\t", StringComparison.Ordinal)
                && line.Contains(signature, StringComparison.Ordinal)),
            $"Windows did not register Ctrl+Shift+{key} for '{action}'");
    }
}
