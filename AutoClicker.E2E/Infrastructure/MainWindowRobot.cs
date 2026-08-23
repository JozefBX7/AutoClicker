// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class MainWindowRobot(AutoClickerE2ESession session)
{
    internal string Mode => Button("Mode").Name;
    internal string Status => Element("Status").Name;
    internal string AdvancedStatus => Element("GlobalEditorBackdrop").Name;
    internal bool StartEnabled => Button("Start").IsEnabled;
    internal bool StopEnabled => Button("Stop").IsEnabled;
    internal bool FiniteRepeatEnabled => Element("RepeatCountMode").IsEnabled;
    internal string SelectedInput => Element("InputAction").AsComboBox().SelectedItem?.Name ?? string.Empty;

    internal void SwitchMode()
    {
        var before = Mode;
        Button("Mode").Invoke();
        session.WaitFor(() => !string.Equals(Mode, before, StringComparison.Ordinal), "mode did not change");
    }

    internal void SelectInput(string name) => SelectComboItem("InputAction", name);
    internal void SelectActionType(string name) => SelectComboItem("ActionType", name);

    internal void SelectCustomKey(VirtualKeyShort key)
    {
        SelectInput("Custom key");
        session.Window.Focus();
        Keyboard.Press(key);
        session.WaitFor(() => SelectedInput.StartsWith("Key:", StringComparison.Ordinal), "custom input key was not captured");
    }

    internal void SetFiniteRepeat(int count)
    {
        Element("RepeatCountMode").AsRadioButton().Click();
        SubmitText("RepeatCount", count.ToString());
    }

    internal void SetRepeatUntilStopped() => Element("RepeatUntilStopped").AsRadioButton().Click();

    internal void DisableTargetWindow()
    {
        var box = Element("TargetWindowEnabled").AsCheckBox();
        if (box.IsChecked == true) box.Click();
    }

    internal void SetFixedPosition(int x, int y)
    {
        Element("FixedPositionMode").AsRadioButton().Click();
        TextBox("CursorX").Text = x.ToString();
        SubmitText("CursorY", y.ToString());
    }

    internal void SetIntervalMilliseconds(int value) => SubmitText("IntervalMilliseconds", value.ToString());

    internal void Start()
    {
        Button("Start").Invoke();
        session.WaitFor(() => !StartEnabled && StopEnabled, "simple action did not enter the running state");
    }

    internal void TryStart() => Button("Start").Invoke();

    internal void Stop()
    {
        Button("Stop").Invoke();
        WaitUntilStopped();
    }

    internal void WaitUntilStopped() =>
        session.WaitFor(() => StartEnabled && !StopEnabled, "simple action did not stop");

    internal void StartAdvancedAction(string actionId)
    {
        Button($"StartAction_{actionId}").Invoke();
        session.WaitFor(() => Button($"StopAction_{actionId}").IsEnabled, $"action '{actionId}' did not start");
    }

    internal void StopAdvancedAction(string actionId)
    {
        Button($"StopAction_{actionId}").Invoke();
        session.WaitFor(() => Button($"StartAction_{actionId}").IsEnabled, $"action '{actionId}' did not stop");
    }

    internal void OpenSettings() => ClickMainButton("Settings");
    internal void OpenAdvancedHelp() => ClickMainButton("AdvancedHelp");
    internal void OpenSequenceEditor()
    {
        // WPF blocks an automation Selection call until the modal ShowDialog handler returns. The editor command
        // is deliberately the final combo item, so a focused End key follows the real keyboard path and opens it
        // without tying up the UI Automation provider needed to drive the dialog.
        Element("InputAction").AsComboBox().Focus();
        Keyboard.Press(VirtualKeyShort.END);
    }
    internal void CollapseSettings() => Button("CollapseSettings").Invoke();
    internal void ToggleTheme() => Button("Theme").Invoke();
    internal void TogglePin() => Button("Pin").Invoke();
    internal void OpenInputJitter() => ClickMainButton("InputJitter");
    internal void OpenInputPulse() => ClickMainButton("InputPulse");
    internal void OpenPositionPicker() => ClickMainButton("PickPosition");
    internal void OpenTargetWindowPicker() => ClickMainButton("FindTargetWindow");

    internal void SaveAsDefault()
    {
        ClickMainButton("SetAsDefault");
        var confirmation = session.Dialog("Confirmation");
        confirmation.FindFirstDescendant(condition => condition.ByAutomationId("ConfirmButton"))!.AsButton().Invoke();
        session.WaitFor(() => Status.Contains("saved as the default", StringComparison.OrdinalIgnoreCase),
            "default settings were not saved");
    }

    internal void CaptureHotkey(VirtualKeyShort key)
    {
        Button("EditHotkey").Invoke();
        session.Window.Focus();
        Keyboard.Press(key);
        session.WaitFor(() => !string.Equals(session.MainElement("HotkeyLabel").Name, "F6", StringComparison.Ordinal),
            "hotkey capture did not complete");
    }

    private void SelectComboItem(string automationId, string name)
    {
        var combo = Element(automationId).AsComboBox();
        combo.Select(name);
        session.WaitFor(() => string.Equals(combo.SelectedItem?.Name, name, StringComparison.Ordinal),
            $"combo '{automationId}' did not select '{name}'");
    }

    private void SubmitText(string automationId, string value)
    {
        var field = TextBox(automationId);
        field.Focus();
        field.Text = value;
        Keyboard.Press(VirtualKeyShort.RETURN);
        session.WaitFor(() => !field.Properties.HasKeyboardFocus.ValueOrDefault, $"Enter did not submit '{automationId}'");
    }

    private AutomationElement Element(string automationId) => session.MainElement(automationId);
    private Button Button(string automationId) => Element(automationId).AsButton();
    private TextBox TextBox(string automationId) => Element(automationId).AsTextBox();

    private void ClickMainButton(string automationId)
    {
        session.Window.Focus();
        Button(automationId).Click();
    }
}
