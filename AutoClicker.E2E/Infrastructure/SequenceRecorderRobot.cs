// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class SequenceRecorderRobot
{
    private readonly AutoClickerE2ESession session;
    private readonly Window window;

    internal SequenceRecorderRobot(AutoClickerE2ESession session)
    {
        this.session = session;
        window = session.Dialog("Record sequence");
    }

    internal string Status => Element(SequenceRecorderAutomationIds.Status).Name;
    internal string EventCount => Element(SequenceRecorderAutomationIds.EventCount).Name;
    internal IReadOnlyList<string> RecordedEventNames =>
        RecordedEvents.Items.Select(item => item.Name).ToList();
    internal int SelectedRecordedEventCount => RecordedEvents.Items.Count(item => item.IsSelected);
    internal int SelectedRecordedEventIndex => Array.FindIndex(RecordedEvents.Items, item => item.IsSelected);
    internal bool TreatBriefTapsAsPressesEnabled =>
        Element(SequenceRecorderAutomationIds.TreatBriefTapsAsPresses).AsCheckBox().IsChecked == true;

    internal void SetIncludeDelays(bool enabled)
    {
        var box = Element(SequenceRecorderAutomationIds.IncludeDelays).AsCheckBox();
        if ((box.IsChecked == true) != enabled) box.Click();
    }

    internal void SetTreatBriefTapsAsPresses(bool enabled)
    {
        var box = Element(SequenceRecorderAutomationIds.TreatBriefTapsAsPresses).AsCheckBox();
        if ((box.IsChecked == true) != enabled) box.Click();
    }

    internal void Start() => Button(SequenceRecorderAutomationIds.Start).Invoke();

    internal void RecordKey(VirtualKeyShort key, int heldMilliseconds)
    {
        window.Focus();
        using (Keyboard.Pressing(key)) Thread.Sleep(heldMilliseconds);
    }

    internal void RecordMouseButton(MouseButton button, int heldMilliseconds)
    {
        var bounds = window.BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(bounds.Left + 12, bounds.Top + 110));
        Mouse.Down(button);
        try { Thread.Sleep(heldMilliseconds); }
        finally { Mouse.Up(button); }
    }

    internal void RecordVerticalScroll(double distance)
    {
        var bounds = window.BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(bounds.Left + 12, bounds.Top + 110));
        Mouse.Scroll(distance);
    }

    internal void RecordHorizontalScroll(double distance)
    {
        var bounds = window.BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(bounds.Left + 12, bounds.Top + 110));
        Mouse.HorizontalScroll(distance);
    }

    internal void Stop() => Button(SequenceRecorderAutomationIds.Stop).Invoke();
    internal void UseRecording() => Button(SequenceRecorderAutomationIds.Use).Invoke();

    internal void SelectRecordedTogether(params int[] indices)
    {
        if (indices.Length == 0) return;
        var items = RecordedEvents.Items;
        items[indices[0]].Select();
        using var control = Keyboard.Pressing(VirtualKeyShort.CONTROL);
        foreach (var index in indices.Skip(1)) items[index].Click();
        session.WaitFor(() => SelectedRecordedEventCount == indices.Length, "recorded events were not multi-selected");
    }

    internal void ClickRecordedEvent(int index) => RecordedEvents.Items[index].Click();
    internal void PressRecordedEventArrowDown() => Keyboard.Press(VirtualKeyShort.DOWN);

    internal void DeleteSelectedFromContextMenu(int index)
    {
        RecordedEvents.Items[index].RightClick();
        session.DesktopElement(SequenceRecorderAutomationIds.DeleteRecordedEvents).AsMenuItem().Invoke();
    }

    internal void ArmRecordedEventDelete(int index)
    {
        RowButton(index, SequenceRecorderAutomationIds.DeleteRecordedEvent).Invoke();
        session.WaitFor(
            () => TryRowButton(index, SequenceRecorderAutomationIds.ConfirmDeleteRecordedEvent) is not null,
            "the recorded-event delete button did not enter its confirmation state");
    }

    internal void CancelRecordedEventDelete(int index) =>
        RowButton(index, SequenceRecorderAutomationIds.CancelDeleteRecordedEvent).Invoke();

    internal void ConfirmRecordedEventDelete(int index) =>
        RowButton(index, SequenceRecorderAutomationIds.ConfirmDeleteRecordedEvent).Invoke();

    private AutomationElement Element(string automationId) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
        $"sequence-recorder element '{automationId}' was not found");

    private Button Button(string automationId) => Element(automationId).AsButton();
    private ListBox RecordedEvents => Element(SequenceRecorderAutomationIds.RecordedEvents).AsListBox();
    private Button RowButton(int index, string automationId) => WaitUntilNotNull(
        () => TryRowButton(index, automationId),
        $"recorded-event row {index} button '{automationId}' was not found");
    private Button? TryRowButton(int index, string automationId) =>
        RecordedEvents.Items[index].FindFirstDescendant(condition => condition.ByAutomationId(automationId))?.AsButton();

    private T WaitUntilNotNull<T>(Func<T?> find, string failure) where T : class
    {
        T? result = null;
        session.WaitFor(() => (result = find()) is not null, failure);
        return result!;
    }
}
