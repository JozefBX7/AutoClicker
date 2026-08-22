// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class SafeMacroExecutionTests
{
    [TestMethod]
    public void SimpleFiniteMouseRun_AutoCompletesAndRecordsPacketsWithoutNativeInput()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectInput("Left click");
        app.SelectActionType("Single");
        app.SetIntervalMilliseconds(60);
        app.SetFixedPosition(123, 234);
        app.SetFiniteRepeat(5);

        app.Start();
        app.WaitUntilStopped();

        var events = fixture.ReadRuntimeEvents();
        Assert.AreEqual(10, events.Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "five immediate clicks should record one down and one up dispatch each");
        Assert.AreEqual(5, events.Count(line => line.Contains("\tcursor\tx=123;y=234", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SimpleRunUntilStopped_StartAndStopToggleTheWorkerAndReleaseHeldInput()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.DisableTargetWindow();
        app.SelectInput("Space");
        app.SelectActionType("Hold");
        app.SetRepeatUntilStopped();

        app.Start();
        session.WaitFor(() => fixture.ReadRuntimeEvents().Any(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "held input did not reach the safe E2E sink");
        app.Stop();

        Assert.IsTrue(fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)) >= 2,
            "a held action should record both its down packet and final release packet");
    }

    [TestMethod]
    public void AdvancedActions_CanRunConcurrentlyAndStopIndependently()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);

        session.Editor.Select(EditorScope.Hotkey);
        app.DisableTargetWindow();
        app.StartAdvancedAction(ProfileE2EFixture.ActionId);
        app.StartAdvancedAction(ProfileE2EFixture.SecondActionId);
        session.WaitFor(() => fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)) >= 2,
            "concurrent actions did not reach the safe E2E sink");

        app.StopAdvancedAction(ProfileE2EFixture.ActionId);
        Assert.IsTrue(session.MainElement($"StopAction_{ProfileE2EFixture.SecondActionId}").AsButton().IsEnabled);
        app.StopAdvancedAction(ProfileE2EFixture.SecondActionId);
    }
}
