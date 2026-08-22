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
            new[] { "Left click", "Wait 25 ms", "Space", "Right click" },
            editor.StepNames.ToArray());
        editor.SetDelayStep(1, 35);
        editor.SelectStep(3);
        editor.MoveSelectedUp();
        CollectionAssert.AreEqual(
            new[] { "Left click", "Wait 35 ms", "Right click", "Space" },
            editor.StepNames.ToArray());
        editor.MoveSelectedDown();
        CollectionAssert.AreEqual(
            new[] { "Left click", "Wait 35 ms", "Space", "Right click" },
            editor.StepNames.ToArray());
        editor.SetUseGlobalPulse(false);
        editor.SavePreset("E2E routine");
        editor.UseSequence();

        Assert.AreEqual("Custom sequence", app.SelectedInput);
        var saved = fixture.ReadSequenceLibrary().Single();
        Assert.AreEqual("E2E routine", saved.Name);
        Assert.AreEqual(4, saved.Steps.Count);
        CollectionAssert.AreEqual(
            new[] { "Left", "Delay", "Custom", "Right" },
            saved.Steps.Select(step => step.Input).ToArray());
        Assert.AreEqual(35, saved.Steps[1].DelayAfterMilliseconds);
        Assert.AreEqual((int)VirtualKeyShort.SPACE, saved.Steps[2].CustomKey);
        Assert.IsFalse(saved.UseGlobalInputPulse);

        app.DisableTargetWindow();
        app.SetIntervalMilliseconds(40);
        app.SetFiniteRepeat(2);
        app.Start();
        app.WaitUntilStopped();
        Assert.AreEqual(6, fixture.ReadRuntimeEvents().Count(line => line.Contains("\tinput\t", StringComparison.Ordinal)),
            "three immediate input steps repeated twice should reach the safe sink six times");

        app.OpenSequenceEditor();
        editor = new SequenceEditorRobot(session);
        editor.SelectPreset("E2E routine");
        editor.LoadPreset();
        Assert.AreEqual(4, editor.StepCount);
        CollectionAssert.AreEqual(
            new[] { "Left click", "Wait 35 ms", "Space", "Right click" },
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
        editor.Cancel();
    }
}
