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
        app.SelectInput(AutomationInputLabels.LeftClick);
        app.SelectActionType(AutomationActionTypeIds.Single);
        app.SetIntervalMilliseconds(60);
        app.SetFixedPosition(123, 234);
        app.SetFiniteRepeat(5);

        app.TryStart();
        session.WaitFor(
            () => fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)) >= 10,
            "the finite mouse run did not emit five clicks through the safe E2E input sink");
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
        app.SelectInput(AutomationInputIds.Space);
        app.SelectActionType(AutomationActionTypeIds.Hold);
        app.SetRepeatUntilStopped();

        app.Start();
        session.WaitFor(() => fixture.ReadRuntimeEvents().Any(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "held input did not reach the safe E2E sink");
        app.Stop();

        var inputEvents = fixture.ReadRuntimeEvents().Where(IsInputEvent).ToList();
        Assert.IsTrue(inputEvents.Count(line => line.Contains("keyboard:vk=0:scan=57:flags=8", StringComparison.Ordinal)) >= 1,
            "the held Space action did not emit a key-down packet");
        Assert.AreEqual(1, inputEvents.Count(line => line.Contains("keyboard:vk=0:scan=57:flags=10", StringComparison.Ordinal)),
            "stopping the held Space action did not emit exactly one key-up packet");
        Assert.IsTrue(inputEvents.Count >= 2
            && inputEvents.Take(inputEvents.Count - 1).All(line => line.Contains("flags=8", StringComparison.Ordinal))
            && inputEvents[^1].Contains("flags=10", StringComparison.Ordinal),
            "held-key repeats must remain key-down packets and the cleanup release must be last");
        var countAfterStop = inputEvents.Count;
        Thread.Sleep(250);
        Assert.AreEqual(countAfterStop, fixture.ReadRuntimeEvents().Count(IsInputEvent),
            "the held action continued emitting input after its cleanup release");
    }

    [TestMethod]
    public void AdvancedActions_CanRunConcurrentlyAndStopIndependently()
    {
        using var fixture = new ProfileE2EFixture();
        var document = fixture.ReadProfiles();
        var profile = document.Profiles.Single(item => item.Id == ProfileE2EFixture.ProfileId);
        var first = profile.Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);
        var second = profile.Actions.Single(item => item.Id == ProfileE2EFixture.SecondActionId);
        first.Settings.TargetExecutable = string.Empty;
        first.Settings.TargetWindowEnabled = false;
        first.Settings.RepeatUntilStopped = true;
        second.Settings.Input = AutomationInputIds.Right;
        second.Settings.MouseButton = AutomationInputIds.Right;
        second.Settings.TargetExecutable = string.Empty;
        second.Settings.TargetWindowEnabled = false;
        second.Settings.RepeatUntilStopped = true;
        AutomationProfileStore.Save(fixture.TestFile(ConfigurationFileNames.AutomationProfiles), document);

        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);

        app.StartAdvancedAction(ProfileE2EFixture.ActionId);
        app.StartAdvancedAction(ProfileE2EFixture.SecondActionId);
        session.WaitFor(() => LeftClickCount(fixture) >= 2 && RightClickCount(fixture) >= 2,
            "both distinguishable concurrent actions did not reach the safe E2E sink");

        app.StopAdvancedAction(ProfileE2EFixture.ActionId);
        Assert.IsTrue(session.MainElement($"StopAction_{ProfileE2EFixture.SecondActionId}").AsButton().IsEnabled);
        var leftCountAfterStop = LeftClickCount(fixture);
        var rightCountAfterFirstStop = RightClickCount(fixture);
        session.WaitFor(() => RightClickCount(fixture) > rightCountAfterFirstStop,
            "stopping the left-click action also stopped the concurrent right-click action");
        Assert.AreEqual(leftCountAfterStop, LeftClickCount(fixture),
            "the first action continued emitting left clicks after it was stopped");

        app.StopAdvancedAction(ProfileE2EFixture.SecondActionId);
        var rightCountAfterStop = RightClickCount(fixture);
        Thread.Sleep(250);
        Assert.AreEqual(rightCountAfterStop, RightClickCount(fixture),
            "the second action continued emitting right clicks after it was stopped");
    }

    private static bool IsInputEvent(string line) => line.Contains("\tinput\t", StringComparison.Ordinal);

    private static int LeftClickCount(ProfileE2EFixture fixture) =>
        fixture.ReadRuntimeEvents().Count(line => IsInputEvent(line) && line.Contains("mouse:2", StringComparison.Ordinal));

    private static int RightClickCount(ProfileE2EFixture fixture) =>
        fixture.ReadRuntimeEvents().Count(line => IsInputEvent(line) && line.Contains("mouse:8", StringComparison.Ordinal));
}
