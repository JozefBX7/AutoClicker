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
public sealed class SequenceEditorFlowTests
{
    [TestMethod]
    public void BuildReorderSaveRunAndDeleteSequence_PersistsTheLibraryAndUsesSafeExecution()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        editor.AddLeft();
        editor.AddDelay(25);
        editor.AddKeyboardKey(VirtualKeyShort.SPACE);
        editor.AddRight();
        Assert.AreEqual(4, editor.StepCount);
        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.LeftClick, "Wait 25 ms", AutomationInputIds.Space, AutomationInputLabels.RightClick },
            editor.StepNames.ToArray());
        editor.SetDelayStep(1, 35);
        editor.SelectStep(3);
        editor.MoveSelectedUp();
        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.LeftClick, "Wait 35 ms", AutomationInputLabels.RightClick, AutomationInputIds.Space },
            editor.StepNames.ToArray());
        editor.MoveSelectedDown();
        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.LeftClick, "Wait 35 ms", AutomationInputIds.Space, AutomationInputLabels.RightClick },
            editor.StepNames.ToArray());
        editor.SetUseGlobalPulse(false);
        editor.SavePreset("E2E routine");
        editor.UseSequence();

        Assert.AreEqual(AutomationInputLabels.CustomSequence, app.SelectedInput);
        var saved = fixture.ReadSequenceLibrary().Single();
        Assert.AreEqual("E2E routine", saved.Name);
        Assert.AreEqual(4, saved.Steps.Count);
        CollectionAssert.AreEqual(
            new[] { AutomationInputIds.Left, AutomationInputIds.Delay, AutomationInputIds.Custom, AutomationInputIds.Right },
            saved.Steps.Select(step => step.Input).ToArray());
        Assert.AreEqual(35, saved.Steps[1].DelayAfterMilliseconds);
        Assert.AreEqual((int)VirtualKeyShort.SPACE, saved.Steps[2].CustomKey);
        Assert.IsFalse(saved.UseGlobalInputPulse);

        app.DisableTargetWindow();
        app.SetIntervalMilliseconds(40);
        app.SetFiniteRepeat(2);
        app.TryStart();
        session.WaitFor(
            () => fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)) >= 6,
            "the saved sequence did not execute twice through the safe E2E input sink");
        app.WaitUntilStopped();
        Assert.AreEqual(6, fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "three immediate input steps repeated twice should reach the safe sink six times");

        app.OpenSequenceEditor();
        editor = new SequenceEditorRobot(session);
        editor.SelectPreset("E2E routine");
        editor.LoadPreset();
        Assert.AreEqual(4, editor.StepCount);
        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.LeftClick, "Wait 35 ms", AutomationInputIds.Space, AutomationInputLabels.RightClick },
            editor.StepNames.ToArray());
        editor.DeleteSelectedPreset();
        var confirmation = session.Dialog("Confirmation");
        confirmation.FindFirstDescendant(condition => condition.ByAutomationId("ConfirmButton"))!.AsButton().Invoke();
        editor.Cancel();

        session.WaitFor(() => fixture.ReadSequenceLibrary().Count == 0, "deleted sequence preset was not persisted");
        Assert.AreEqual(0, fixture.ReadSequenceLibrary().Count);
    }

    [TestMethod]
    public void SequenceEditor_ValidatesMinimumStepsAndSupportsRemove()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        editor.AddMiddle();
        editor.UseSequence();
        Assert.AreEqual(1, editor.StepCount, "the editor should stay open when fewer than two steps are present");
        editor.AddRight();
        editor.SelectStep(0);
        editor.RemoveSelected();
        Assert.AreEqual(1, editor.StepCount);
        editor.SelectStep(0);
        editor.SetSelectedMode("Hold down");
        editor.AddLeft();
        editor.SelectStep(1);
        editor.SetSelectedMode("Release");
        editor.UseSequence();
        Assert.AreEqual(2, editor.StepCount, "a mismatched release should keep the editor open");
        StringAssert.Contains(editor.Hint, "matching Hold");
        editor.Cancel();
    }

    [TestMethod]
    public void ContextMenu_MultiSelectClipboardGroupDragReleaseAndPreview_WorkTogether()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        editor.AddLeft();
        editor.AddDelay(100);
        editor.AddRight();
        editor.AddKeyboardKey(VirtualKeyShort.SPACE);
        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.LeftClick, "Wait 100 ms", AutomationInputLabels.RightClick, AutomationInputIds.Space },
            editor.StepNames.ToArray());
        StringAssert.Contains(editor.TimelinePreview, "4 events");
        StringAssert.Contains(editor.TimelinePreview, "100 ms");

        editor.SelectStep(0);
        editor.SetSelectedMode("Hold down");
        Assert.IsTrue(editor.ContextMenuContains(0, SequenceEditorAutomationIds.AddMatchingRelease));
        editor.ChooseContextMenu(0, SequenceEditorAutomationIds.AddMatchingRelease);
        CollectionAssert.AreEqual(
            new[] { "Hold left mouse", "Wait 100 ms", AutomationInputLabels.RightClick, AutomationInputIds.Space, "Release left mouse" },
            editor.StepNames.ToArray());
        StringAssert.Contains(editor.TimelinePreview, "Left click 0 ms–100 ms");
        Assert.IsFalse(editor.ContextMenuContains(0, SequenceEditorAutomationIds.AddMatchingRelease),
            "a hold with a later release should not offer another matching release");

        editor.SelectTogether(1, 2);
        Assert.AreEqual(2, editor.SelectedStepCount);
        editor.DragSelectedAfter(1, 4);
        var reorderedNames = editor.StepNames.ToArray();
        CollectionAssert.AreEqual(
            new[] { "Hold left mouse", AutomationInputIds.Space, "Release left mouse", "Wait 100 ms", AutomationInputLabels.RightClick },
            reorderedNames,
            $"Group drag produced: {string.Join(" | ", reorderedNames)}. Hint: {editor.Hint}");
        Assert.AreEqual(2, editor.SelectedStepCount, "dragging should preserve the moved group selection");

        editor.ChooseContextMenu(3, SequenceEditorAutomationIds.DuplicateSelected);
        CollectionAssert.AreEqual(
            new[]
            {
                "Hold left mouse", AutomationInputIds.Space, "Release left mouse", "Wait 100 ms", AutomationInputLabels.RightClick,
                "Wait 100 ms", AutomationInputLabels.RightClick
            },
            editor.StepNames.ToArray());
        Assert.AreEqual(2, editor.SelectedStepCount);

        editor.ChooseContextMenu(5, SequenceEditorAutomationIds.CopySelected);
        editor.ChooseContextMenu(5, SequenceEditorAutomationIds.RemoveSelected);
        Assert.AreEqual(5, editor.StepCount);
        editor.ChooseContextMenu(1, SequenceEditorAutomationIds.Paste);
        CollectionAssert.AreEqual(
            new[]
            {
                "Hold left mouse", AutomationInputIds.Space, "Wait 100 ms", AutomationInputLabels.RightClick,
                "Release left mouse", "Wait 100 ms", AutomationInputLabels.RightClick
            },
            editor.StepNames.ToArray(),
            "context-menu paste should insert immediately after the event that opened the menu");

        editor.ChooseContextMenu(0, SequenceEditorAutomationIds.SelectAll);
        Assert.AreEqual(7, editor.SelectedStepCount);
        editor.Cancel();
    }

    [TestMethod]
    public void EventListScrollbar_DragsWithoutStartingASequenceReorder()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        for (var milliseconds = 1; milliseconds <= 20; milliseconds++) editor.AddDelay(milliseconds);
        var originalOrder = editor.StepNames.ToArray();
        editor.SelectStep(0);

        editor.DragVerticalScrollbarDown();

        CollectionAssert.AreEqual(originalOrder, editor.StepNames.ToArray(),
            "dragging the scrollbar must scroll the list without moving sequence events");
        editor.Cancel();
    }

    [TestMethod]
    public void UnselectedEvent_CanBeDraggedDirectlyToReorderTheSequence()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        editor.AddLeft();
        editor.AddDelay(25);
        editor.AddRight();
        editor.AddKeyboardKey(VirtualKeyShort.SPACE);

        editor.DragSelectedAfter(0, 3);

        var reorderedNames = editor.StepNames.ToArray();
        CollectionAssert.AreEqual(
            new[] { "Wait 25 ms", AutomationInputLabels.RightClick, AutomationInputIds.Space, AutomationInputLabels.LeftClick },
            reorderedNames,
            $"dragging an unselected event should select and move that event in one interaction. "
            + $"Actual: {string.Join(" | ", reorderedNames)}. Selected: {editor.SelectedStepCount}. Hint: {editor.Hint}");

        editor.DragSelectedOutside(0);
        CollectionAssert.AreEqual(reorderedNames, editor.StepNames.ToArray(),
            "releasing a dragged event outside the sequence list should cancel the reorder");
        editor.Cancel();
    }

    [TestMethod]
    public void StatefulSequence_RepeatsHeldKeysExecutesReleaseAndCleansUpOnCancellation()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSequenceEditor();
        var editor = new SequenceEditorRobot(session);

        editor.AddKeyboardKey(VirtualKeyShort.KEY_A);
        editor.SetSelectedMode("Hold down");
        editor.AddDelay(600);
        editor.AddKeyboardKey(VirtualKeyShort.KEY_A);
        editor.SetSelectedMode("Release");
        editor.AddLeft();
        editor.SetSelectedMode("Hold down");
        editor.AddDelay(5_000);
        editor.AddLeft();
        editor.SetSelectedMode("Release");
        editor.SavePreset("Held inputs");
        editor.UseSequence();
        session.WaitFor(() => fixture.ReadSequenceLibrary().Count == 1,
            "the stateful sequence preset was not persisted after the editor closed");

        var saved = fixture.ReadSequenceLibrary().Single().Steps;
        CollectionAssert.AreEqual(
            new[]
            {
                SequenceStepMode.Hold,
                SequenceStepMode.Press,
                SequenceStepMode.Release,
                SequenceStepMode.Hold,
                SequenceStepMode.Press,
                SequenceStepMode.Release
            },
            saved.Select(step => step.Mode).ToArray());

        app.DisableTargetWindow();
        app.SetFiniteRepeat(1);
        app.Start();
        session.WaitFor(() =>
        {
            var events = fixture.ReadRuntimeEvents();
            return events.Count(line => line.Contains("keyboard:", StringComparison.Ordinal) && line.Contains("flags=8", StringComparison.Ordinal)) >= 2
                && events.Any(line => line.Contains("keyboard:", StringComparison.Ordinal) && line.Contains("flags=10", StringComparison.Ordinal))
                && events.Any(line => line.Contains("mouse:2", StringComparison.Ordinal));
        }, "the held key did not repeat/release or the held mouse event did not start");

        app.Stop();
        session.WaitFor(() => fixture.ReadRuntimeEvents().Any(line => line.Contains("mouse:4", StringComparison.Ordinal)),
            "cancelling the sequence did not release its held mouse input");

        var inputEvents = fixture.ReadRuntimeEvents().Where(line => line.Contains("\tinput\t", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(1, inputEvents.Count(line => line.Contains("mouse:2", StringComparison.Ordinal)),
            "the sequence should press its held mouse input exactly once");
        Assert.AreEqual(1, inputEvents.Count(line => line.Contains("mouse:4", StringComparison.Ordinal)),
            "cancelling the sequence should release its held mouse input exactly once");
        Assert.AreEqual(1, inputEvents.Count(line => line.Contains("keyboard:", StringComparison.Ordinal) && line.Contains("flags=10", StringComparison.Ordinal)),
            "the explicit keyboard release event should emit exactly one key-up packet");
        Assert.IsTrue(
            inputEvents.FindIndex(line => line.Contains("mouse:2", StringComparison.Ordinal))
            < inputEvents.FindIndex(line => line.Contains("mouse:4", StringComparison.Ordinal)),
            "the held mouse cleanup release must occur after its down packet");
    }
}
