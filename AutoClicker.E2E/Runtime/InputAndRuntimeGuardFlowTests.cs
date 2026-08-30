// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class InputAndRuntimeGuardFlowTests
{
    [DataTestMethod]
    [DataRow(AutomationInputLabels.LeftClick, "mouse:2", "mouse:4")]
    [DataRow(AutomationInputLabels.RightClick, "mouse:8", "mouse:16")]
    [DataRow(AutomationInputLabels.MiddleClick, "mouse:32", "mouse:64")]
    [DataRow(AutomationInputLabels.Mouse4Click, "mouse:128:data=1", "mouse:256:data=1")]
    [DataRow(AutomationInputLabels.Mouse5Click, "mouse:128:data=2", "mouse:256:data=2")]
    [DataRow(AutomationInputIds.Space, "scan=57:flags=8", "scan=57:flags=10")]
    [DataRow(AutomationInputIds.Enter, "scan=28:flags=8", "scan=28:flags=10")]
    public void EveryDirectInput_CanRunFiniteAndAutoCompleteThroughTheSafeSink(
        string input,
        string downSignature,
        string upSignature)
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectInput(input);
        app.SelectActionType(AutomationActionTypeIds.Single);
        app.SetIntervalMilliseconds(100);
        app.SetFiniteRepeat(5);

        app.Start();
        app.WaitUntilStopped();

        var events = fixture.ReadRuntimeEvents().Where(line => line.Contains("\tinput\t", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(10, events.Count);
        Assert.AreEqual(5, events.Count(line => line.Contains(downSignature, StringComparison.Ordinal)),
            $"{input} did not generate the expected down packet");
        Assert.AreEqual(5, events.Count(line => line.Contains(upSignature, StringComparison.Ordinal)),
            $"{input} did not generate the expected up packet");
    }

    [DataTestMethod]
    [DataRow(AutomationInputLabels.ScrollUp, "mouse:2048:data=120")]
    [DataRow(AutomationInputLabels.ScrollDown, "mouse:2048:data=-120")]
    [DataRow(AutomationInputLabels.ScrollLeft, "mouse:4096:data=-120")]
    [DataRow(AutomationInputLabels.ScrollRight, "mouse:4096:data=120")]
    public void EveryDirectScrollInput_CanRunFiniteAndAutoCompleteThroughTheSafeSink(string input, string signature)
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectInput(input);
        app.SelectActionType(AutomationActionTypeIds.Single);
        app.SetIntervalMilliseconds(100);
        app.SetFiniteRepeat(5);

        app.Start();
        app.WaitUntilStopped();

        var events = fixture.ReadRuntimeEvents().Where(line => line.Contains("\tinput\t", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(5, events.Count);
        Assert.AreEqual(5, events.Count(line => line.Contains(signature, StringComparison.Ordinal)),
            $"{input} did not generate the expected wheel packet");
    }

    [TestMethod]
    public void DoubleClick_DispatchesTwoCompletePressesPerRepeat()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectInput(AutomationInputLabels.LeftClick);
        app.SelectActionType(AutomationActionTypeIds.Double);
        app.SetIntervalMilliseconds(100);
        app.SetFiniteRepeat(4);
        app.Start();
        app.WaitUntilStopped();
        Assert.AreEqual(16, fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PickedCustomKey_CanBeCapturedAndExecutedSafely()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectCustomKey(VirtualKeyShort.KEY_A);
        app.SetIntervalMilliseconds(100);
        app.SetFiniteRepeat(4);
        app.Start();
        app.WaitUntilStopped();
        Assert.AreEqual(8, fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void HoldWithTargetWindow_IsRejectedBeforeAWorkerOrInputStarts()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.SelectInput(AutomationInputLabels.LeftClick);
        app.SelectActionType(AutomationActionTypeIds.Hold);
        app.TryStart();
        session.WaitFor(() => app.Status.Contains("does not support held input", StringComparison.OrdinalIgnoreCase),
            "target-window hold validation was not shown");
        Assert.IsTrue(app.StartEnabled);
        Assert.IsFalse(app.StopEnabled);
        Assert.AreEqual(0, fixture.ReadRuntimeEvents().Count);
    }

    [TestMethod]
    public void MismatchedTargetWindow_SuppressesInputUntilTheRunIsStopped()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.SelectInput(AutomationInputLabels.LeftClick);
        app.SelectActionType(AutomationActionTypeIds.Single);
        app.SetRepeatUntilStopped();
        app.Start();
        Thread.Sleep(250);
        Assert.AreEqual(0, fixture.ReadRuntimeEvents().Count);
        app.Stop();
    }

    [TestMethod]
    public void SettingsAndProfileManagement_AreBlockedWhileAutomationIsActive()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        session.Editor.Select(EditorScope.Hotkey);
        app.DisableTargetWindow();
        app.SetRepeatUntilStopped();
        app.StartAdvancedAction(ProfileE2EFixture.ActionId);

        Assert.IsFalse(session.MainElement("Settings").AsButton().IsEnabled);
        app.OpenSettings();
        Assert.IsFalse(session.IsDialogOpen("Settings"));
        Assert.IsFalse(session.MainElement("NewProfile").AsButton().IsEnabled);
        Assert.IsFalse(session.MainElement("AddHotkey").AsButton().IsEnabled);

        app.StopAdvancedAction(ProfileE2EFixture.ActionId);
    }

}
