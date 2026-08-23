// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.Runtime.InteropServices;

namespace AutoClicker.E2E;

internal sealed class SequenceEditorRobot
{
    private readonly AutoClickerE2ESession session;
    private readonly Window window;

    internal SequenceEditorRobot(AutoClickerE2ESession session)
    {
        this.session = session;
        window = session.Dialog(AutomationInputLabels.CustomSequence);
    }

    internal int StepCount => List(SequenceEditorAutomationIds.Steps).Items.Length;
    internal IReadOnlyList<string> StepNames => List(SequenceEditorAutomationIds.Steps).Items.Select(item => item.Name).ToList();
    internal int SelectedStepCount => List(SequenceEditorAutomationIds.Steps).Items.Count(item => item.IsSelected);
    internal string Hint => Element("HintLabel").Name;
    internal string TimelinePreview => Element(SequenceEditorAutomationIds.TimelinePreview).Name;

    internal void AddLeft() => Button("LeftActionButton").Invoke();
    internal void AddRight() => Button("RightActionButton").Invoke();
    internal void AddMiddle() => Button("MiddleActionButton").Invoke();

    internal void AddKeyboardKey(VirtualKeyShort key)
    {
        Button("KeyboardActionButton").Invoke();
        window.Focus();
        Keyboard.Press(key);
    }

    internal void AddDelay(int milliseconds)
    {
        TextBox("DelayBox").Text = milliseconds.ToString();
        ButtonByName("+ Delay").Invoke();
    }

    internal void SelectStep(int index)
    {
        var item = List(SequenceEditorAutomationIds.Steps).Items[index];
        item.ScrollIntoView();
        session.WaitFor(() => !item.IsOffscreen, $"sequence step {index} did not scroll into view");
        item.Select();
    }
    internal void SelectTogether(params int[] indices)
    {
        if (indices.Length == 0) return;
        var items = List(SequenceEditorAutomationIds.Steps).Items;
        items[indices[0]].Select();
        using var control = Keyboard.Pressing(VirtualKeyShort.CONTROL);
        foreach (var index in indices.Skip(1)) items[index].Click();
        session.WaitFor(() => SelectedStepCount == indices.Length, "sequence steps were not multi-selected");
    }

    internal void ChooseContextMenu(int index, string automationId)
    {
        OpenContextMenu(index);
        session.DesktopElement(automationId).AsMenuItem().Invoke();
    }

    internal bool ContextMenuContains(int index, string automationId)
    {
        OpenContextMenu(index);
        var exists = session.TryDesktopElement(automationId) is not null;
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        return exists;
    }

    internal void DragSelectedAfter(int sourceIndex, int targetIndex)
    {
        var items = List(SequenceEditorAutomationIds.Steps).Items;
        var sourceBounds = items[sourceIndex].BoundingRectangle;
        var start = new System.Drawing.Point(sourceBounds.Left + sourceBounds.Width / 2, sourceBounds.Top + sourceBounds.Height / 2);
        var targetBounds = items[targetIndex].BoundingRectangle;
        var end = new System.Drawing.Point(targetBounds.Left + targetBounds.Width / 2, targetBounds.Bottom - 2);
        DragThroughIntermediatePoints(start, end);
    }

    internal void DragSelectedOutside(int sourceIndex)
    {
        var list = List(SequenceEditorAutomationIds.Steps);
        var sourceBounds = list.Items[sourceIndex].BoundingRectangle;
        var start = new System.Drawing.Point(sourceBounds.Left + sourceBounds.Width / 2, sourceBounds.Top + sourceBounds.Height / 2);
        var end = new System.Drawing.Point(list.BoundingRectangle.Right + 30, sourceBounds.Top + sourceBounds.Height / 2);
        DragThroughIntermediatePoints(start, end);
    }

    internal void DragVerticalScrollbarDown()
    {
        var list = List(SequenceEditorAutomationIds.Steps);
        var bounds = list.BoundingRectangle;
        var scrollbarCenterX = bounds.Right - 8;
        var start = new System.Drawing.Point(scrollbarCenterX, bounds.Top + 18);
        var end = new System.Drawing.Point(scrollbarCenterX, bounds.Bottom - 18);

        Mouse.Drag(start, end, MouseButton.Left);
        session.WaitFor(() => List(SequenceEditorAutomationIds.Steps).Items[0].IsOffscreen,
            "dragging the sequence event list scrollbar did not scroll the first event out of view");
    }

    private static void DragThroughIntermediatePoints(System.Drawing.Point start, System.Drawing.Point end)
    {
        const int movementSteps = 20;
        MoveCursor(start);
        Mouse.Down(MouseButton.Left);
        try
        {
            for (var step = 1; step <= movementSteps; step++)
            {
                MoveCursor(new System.Drawing.Point(
                    start.X + ((end.X - start.X) * step / movementSteps),
                    start.Y + ((end.Y - start.Y) * step / movementSteps)));
                Thread.Sleep(8);
            }
        }
        finally { Mouse.Up(MouseButton.Left); }
    }

    private static void MoveCursor(System.Drawing.Point position)
    {
        if (!SetCursorPos(position.X, position.Y))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    internal void MoveSelectedUp() => Button(SequenceEditorAutomationIds.MoveUp).Invoke();
    internal void MoveSelectedDown() => Button(SequenceEditorAutomationIds.MoveDown).Invoke();
    internal void RemoveSelected() => Button(SequenceEditorAutomationIds.Remove).Invoke();

    internal void SetSelectedMode(string mode)
    {
        var combo = Element(SequenceEditorAutomationIds.StepMode).AsComboBox();
        combo.Select(mode);
        session.WaitFor(() => string.Equals(combo.SelectedItem?.Name, mode, StringComparison.Ordinal),
            $"sequence event mode did not change to '{mode}'");
    }

    internal void SetDelayStep(int index, int milliseconds)
    {
        var item = List(SequenceEditorAutomationIds.Steps).Items[index];
        item.Select();
        var field = item.FindFirstDescendant(condition => condition.ByAutomationId(SequenceEditorAutomationIds.StepDelay))!.AsTextBox();
        field.Focus();
        field.Text = milliseconds.ToString();
        Button(SequenceEditorAutomationIds.MoveUp).Focus();
        session.WaitFor(() => item.Name.Contains(milliseconds.ToString(), StringComparison.Ordinal),
            "edited delay was not reflected in the sequence");
    }

    internal void SetUseGlobalPulse(bool enabled)
    {
        var box = Element("UseGlobalInputPulseCheckBox").AsCheckBox();
        if ((box.IsChecked == true) != enabled) box.Click();
    }

    internal void SavePreset(string name)
    {
        TextBox("PresetNameBox").Text = name;
        ButtonByName("Save").Invoke();
    }

    internal void SelectPreset(string name)
    {
        var combo = Element("PresetCombo").AsComboBox();
        combo.Expand();
        var item = combo.Items.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Sequence preset '{name}' was not found.");
        item.Select();
    }

    internal void LoadPreset() => ButtonByName("Load").Invoke();
    internal void DeleteSelectedPreset()
    {
        Button("DeletePresetButton").Click();
    }
    internal void UseSequence() => ButtonByName("Use sequence").Invoke();
    internal void Cancel() => ButtonByName("Cancel").Invoke();

    private void OpenContextMenu(int index)
    {
        window.Focus();
        var item = List(SequenceEditorAutomationIds.Steps).Items[index];
        item.ScrollIntoView();
        session.WaitFor(() => !item.IsOffscreen, $"sequence step {index} did not scroll into view");
        item.RightClick();
    }

    private AutomationElement Element(string automationId) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
        $"sequence-editor element '{automationId}' was not found");

    private Button Button(string automationId) => Element(automationId).AsButton();
    private TextBox TextBox(string automationId) => Element(automationId).AsTextBox();
    private ListBox List(string automationId) => Element(automationId).AsListBox();
    private Button ButtonByName(string name) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)))?.AsButton(),
        $"sequence-editor button '{name}' was not found");

    private T WaitUntilNotNull<T>(Func<T?> find, string failure) where T : class
    {
        T? result = null;
        session.WaitFor(() => (result = find()) is not null, failure);
        return result!;
    }

    [DllImport(NativeLibraryNames.User32, SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
}
