// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class BehaviorDialogAndPickerFlowTests
{
    [TestMethod]
    public void InputJitterAndPulse_SaveIntoSimpleDefaultsAndReload()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using (var session = fixture.Launch())
        {
            var app = new MainWindowRobot(session);
            app.OpenInputJitter();
            var jitter = session.Dialog("Input jitter");
            jitter.FindFirstDescendant(condition => condition.ByAutomationId("SecondsBox"))!.AsTextBox().Text = "1";
            jitter.FindFirstDescendant(condition => condition.ByAutomationId("MillisecondsBox"))!.AsTextBox().Text = "250";
            Button(jitter, "Save").Invoke();
            session.WaitFor(() => session.MainElement("InputJitter").Name.Contains("1.25", StringComparison.Ordinal),
                "input jitter did not update");

            app.OpenInputPulse();
            var pulse = session.Dialog("Input pulse");
            var combo = pulse.FindFirstDescendant(condition => condition.ByAutomationId("PulseCombo"))!.AsComboBox();
            combo.Expand();
            combo.Items.Single(item => item.Name == "5 ms").Select();
            Button(pulse, "Save").Invoke();
            session.WaitFor(() => session.MainElement("InputPulse").Name.Contains("5 ms", StringComparison.Ordinal),
                "input pulse did not update");

            app.SaveAsDefault();
        }

        Assert.AreEqual(1250L, fixture.ReadSimpleDefaults().InputJitterMaximumMilliseconds);
        Assert.AreEqual(5, fixture.ReadSimpleDefaults().InputPulseMilliseconds);
    }

    [TestMethod]
    public void AdvancedHelp_OpensAndClosesWithoutCrashing()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenAdvancedHelp();
        var help = session.Dialog("Advanced mode help");
        Button(help, "Got it").Invoke();
        Assert.IsFalse(session.Application.HasExited);
        Assert.AreEqual("Advanced", session.MainElement("Mode").Name);
    }

    [TestMethod]
    public void PositionAndTargetPickers_CanBeCancelledWithoutHooksOrConfigurationChanges()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        var originalX = session.Editor.CursorX;
        var originalY = session.Editor.CursorY;

        app.OpenPositionPicker();
        var position = session.Dialog("Pick position");
        position.Focus();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        session.WaitFor(() => !session.IsDialogOpen("Pick position"), "position picker did not close");
        Assert.AreEqual(originalX, session.Editor.CursorX);
        Assert.AreEqual(originalY, session.Editor.CursorY);

        app.OpenTargetWindowPicker();
        var target = session.Dialog("Choose target window");
        Button(target, "Cancel").Invoke();
        Assert.AreEqual("simple.exe", session.Editor.TargetExecutable);
        Assert.IsFalse(session.Application.HasExited);
    }

    private static Button Button(Window window, string name) =>
        window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)))!.AsButton();

}
