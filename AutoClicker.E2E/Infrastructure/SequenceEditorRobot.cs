// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class SequenceEditorRobot
{
    private readonly AutoClickerE2ESession session;
    private readonly Window window;

    internal SequenceEditorRobot(AutoClickerE2ESession session)
    {
        this.session = session;
        window = session.Dialog("Custom sequence");
    }

    internal int StepCount => List("SequenceSteps").Items.Length;
    internal IReadOnlyList<string> StepNames => List("SequenceSteps").Items.Select(item => item.Name).ToList();

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

    internal void SelectStep(int index) => List("SequenceSteps").Items[index].Select();
    internal void MoveSelectedUp() => Button("MoveSequenceStepUp").Invoke();
    internal void MoveSelectedDown() => Button("MoveSequenceStepDown").Invoke();
    internal void RemoveSelected() => Button("RemoveSequenceStep").Invoke();

    internal void SetDelayStep(int index, int milliseconds)
    {
        var item = List("SequenceSteps").Items[index];
        item.Select();
        var field = item.FindFirstDescendant(condition => condition.ByAutomationId("SequenceStepDelay"))!.AsTextBox();
        field.Focus();
        field.Text = milliseconds.ToString();
        Button("MoveSequenceStepUp").Focus();
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
}
