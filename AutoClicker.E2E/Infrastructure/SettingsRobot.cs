// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class SettingsRobot
{
    private readonly AutoClickerE2ESession session;
    private readonly Window window;

    internal SettingsRobot(AutoClickerE2ESession session)
    {
        this.session = session;
        window = session.Dialog("Settings");
    }

    internal void SelectMode(string mode) => SelectCombo("ModeCombo", mode);
    internal void SelectWorkerPriority(string priority) => SelectCombo("WorkerPriorityCombo", priority);
    internal void SetKeyboardModifiers(bool enabled) => SetCheckBox("EnableKeyboardHotkeyModifiers", enabled);
    internal void SetCrashRecovery(bool enabled) => SetCheckBox("EnableCrashRecovery", enabled);
    internal void SetCadenceDiagnostics(bool enabled) => SetCheckBox("EnableCadenceDiagnostics", enabled);
    internal void SetOpenRgb(bool enabled) => SetCheckBox("EnableOpenRgb", enabled);
    internal void SetOpenRgbAutoStart(bool enabled) => SetCheckBox("AutoStartOpenRgb", enabled);
    internal void SetStopAutoStartedOpenRgb(bool enabled) => SetCheckBox("StopAutoStartedOpenRgb", enabled);
    internal bool OpenRgbOptionsEnabled => Element("AutoStartOpenRgb").IsEnabled;
    internal void SetIndicatorColor(string color) => Element("IndicatorColor").AsTextBox().Text = color;
    internal void SelectLightingEffect(string effect) => SelectCombo("LightingEffect", effect);
    internal void Save() => ButtonByName("Save settings").Invoke();
    internal void Cancel() => ButtonByName("Cancel").Invoke();
    internal void Export(string scope) => Element($"Export{scope}").AsButton().Invoke();
    internal void Restore(string scope) => Element($"Restore{scope}").AsButton().Invoke();

    internal void Reset(string resetButtonName)
    {
        ButtonByName("Reset to defaults").Click();
        var chooser = session.Dialog("Reset options");
        var reset = chooser.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(resetButtonName)))?.AsButton()
            ?? throw new InvalidOperationException($"Reset option '{resetButtonName}' was not found.");
        reset.Click();
        var confirmation = session.Dialog("Confirmation");
        confirmation.FindFirstDescendant(condition => condition.ByAutomationId("ConfirmButton"))!.AsButton().Invoke();
    }

    internal void OpenQuickStart()
    {
        ButtonByName("Open quick start guide").Click();
    }

    private void SelectCombo(string automationId, string name)
    {
        var combo = Element(automationId).AsComboBox();
        combo.Select(name);
        session.WaitFor(() => string.Equals(combo.SelectedItem?.Name, name, StringComparison.Ordinal),
            $"settings combo '{automationId}' did not select '{name}'");
    }

    private void SetCheckBox(string automationId, bool enabled)
    {
        var box = Element(automationId).AsCheckBox();
        if ((box.IsChecked == true) != enabled) box.Toggle();
        session.WaitFor(() => (box.IsChecked == true) == enabled, $"settings checkbox '{automationId}' did not change");
    }

    private AutomationElement Element(string automationId) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
        $"settings element '{automationId}' was not found");

    private Button ButtonByName(string name) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)))?.AsButton(),
        $"settings button '{name}' was not found");

    private T WaitUntilNotNull<T>(Func<T?> find, string failure) where T : class
    {
        T? result = null;
        session.WaitFor(() => (result = find()) is not null, failure);
        return result!;
    }
}
