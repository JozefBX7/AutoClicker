// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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

    [TestMethod]
    public void RegisteredHotkeys_AreIgnoredAcrossModalEditorsAndDialogsThenResumeAfterClosing()
    {
        using var fixture = new ProfileE2EFixture(chordedKeyboardHotkeys: true);
        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var app = new MainWindowRobot(session);
        session.Editor.Select(EditorScope.Hotkey);
        app.DisableTargetWindow();
        app.SetRepeatUntilStopped();

        app.OpenSequenceEditor();
        AssertHotkeyIgnored(session, fixture, VirtualKeyShort.F6, "custom sequence editor");
        new SequenceEditorRobot(session).Cancel();

        app.OpenAdvancedHelp();
        AssertHotkeyIgnored(session, fixture, VirtualKeyShort.F6, "advanced help");
        var help = session.Dialog("Advanced mode help");
        help.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("Got it")))!
            .AsButton().Invoke();

        app.OpenSettings();
        AssertHotkeyIgnored(session, fixture, VirtualKeyShort.F6, "settings");
        new SettingsRobot(session).Cancel();
        session.WaitFor(
            () => session.MainElement($"StartAction_{ProfileE2EFixture.ActionId}").AsButton().IsEnabled,
            "main action controls did not re-enable after the modal dialogs closed");

        ToggleAndAssert(session, fixture, VirtualKeyShort.F6, ProfileE2EFixture.ActionId);
    }

    [TestMethod]
    public void ChangingExistingRegisteredHotkey_RegistersOnceWithoutAFalseConflictAndStillRejectsDuplicates()
    {
        using var fixture = new ProfileE2EFixture(chordedKeyboardHotkeys: true);
        using var session = fixture.Launch(registerKeyboardHotkeys: true);
        var app = new MainWindowRobot(session);
        var actions = new AdvancedActionRobot(session);
        session.Editor.Select(EditorScope.Hotkey);
        app.DisableTargetWindow();
        app.SetRepeatUntilStopped();

        actions.ChangeHotkeyChord(ProfileE2EFixture.ActionId, VirtualKeyShort.F8);
        session.WaitFor(
            () => app.AdvancedStatus.Contains("Ctrl + Shift + F8", StringComparison.Ordinal)
                && !app.AdvancedStatus.Contains("in use", StringComparison.OrdinalIgnoreCase),
            "changed existing hotkey reported a false registration conflict");
        AssertRegistered(session, fixture, VirtualKeyShort.F8, "primary");
        ToggleAndAssert(session, fixture, VirtualKeyShort.F8, ProfileE2EFixture.ActionId);

        actions.ChangeHotkeyChord(ProfileE2EFixture.ActionId, VirtualKeyShort.F7);
        session.WaitFor(
            () => app.AdvancedStatus.Contains("already assigned", StringComparison.OrdinalIgnoreCase),
            "a genuinely duplicate profile hotkey was not rejected");
        ToggleAndAssert(session, fixture, VirtualKeyShort.F8, ProfileE2EFixture.ActionId);
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

    private static void AssertHotkeyIgnored(
        AutoClickerE2ESession session,
        ProfileE2EFixture fixture,
        VirtualKeyShort key,
        string context)
    {
        var inputCount = fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal));
        session.SendRegisteredKeyboardHotkey(key);
        Thread.Sleep(250);
        Assert.AreEqual(inputCount,
            fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            $"registered hotkey executed while {context} was open");
    }
}
