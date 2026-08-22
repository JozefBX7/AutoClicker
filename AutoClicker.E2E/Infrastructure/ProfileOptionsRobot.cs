// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class ProfileOptionsRobot(AutoClickerE2ESession session)
{
    internal void NewProfile() => MainButton("NewProfile").Invoke();

    internal void SaveDraftAs(string name)
    {
        MainButton("SaveProfile").Click();
        var dialog = session.Dialog("Profile name");
        dialog.FindFirstDescendant(condition => condition.ByAutomationId("NameBox"))!.AsTextBox().Text = name;
        ButtonByName(dialog, "Save").Invoke();
        session.WaitFor(() => session.Window.FindFirstDescendant(condition => condition.ByAutomationId("SaveProfile")) is null,
            "profile save indicator did not clear");
    }

    internal void SaveExisting()
    {
        MainButton("SaveProfile").Invoke();
        session.WaitFor(() => session.Window.FindFirstDescendant(condition => condition.ByAutomationId("SaveProfile")) is null,
            "profile save indicator did not clear");
    }

    internal void OpenMenu(string profileId)
    {
        session.Window.Focus();
        MainButton($"Profile_{profileId}").RightClick();
    }

    internal void ChooseMenu(string profileId, string automationId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            OpenMenu(profileId);
            for (var poll = 0; poll < 20; poll++)
            {
                if (session.TryDesktopElement(automationId) is { } item)
                {
                    item.AsMenuItem().Invoke();
                    return;
                }
                Thread.Sleep(75);
            }
            Keyboard.Press(VirtualKeyShort.ESCAPE);
        }
        throw new TimeoutException($"profile menu item '{automationId}' was not found");
    }

    internal void Rename(string profileId, string name)
    {
        ChooseMenu(profileId, "RenameProfile");
        var dialog = session.Dialog("Profile name");
        dialog.FindFirstDescendant(condition => condition.ByAutomationId("NameBox"))!.AsTextBox().Text = name;
        ButtonByName(dialog, "Save").Invoke();
    }

    internal void Confirm()
    {
        var dialog = session.Dialog("Confirmation");
        dialog.FindFirstDescendant(condition => condition.ByAutomationId("ConfirmButton"))!.AsButton().Invoke();
    }

    internal void ConfigureProfileLighting(string profileId, bool enabled, string effect)
    {
        ChooseMenu(profileId, "ConfigureProfileLighting");
        var dialog = session.Dialog("Hotkey lighting");
        var enabledBox = dialog.FindFirstDescendant(condition => condition.ByAutomationId("EnabledCheckBox"))!.AsCheckBox();
        if ((enabledBox.IsChecked == true) != enabled) enabledBox.Toggle();
        var combo = dialog.FindFirstDescendant(condition => condition.ByAutomationId("EffectCombo"))!.AsComboBox();
        combo.Expand();
        combo.Items.Single(item => string.Equals(item.Name, effect, StringComparison.Ordinal)).Select();
        ButtonByName(dialog, "Save override").Invoke();
    }

    internal Window SharedDefaultsDialog() => session.Dialog("Shared behavior defaults");

    internal void RevertOnlyInterval(Window dialog)
    {
        foreach (var id in new[] { "RepeatCheck", "PositionCheck", "TargetWindowCheck", "InputJitterCheck", "InputPulseCheck" })
        {
            var box = dialog.FindFirstDescendant(condition => condition.ByAutomationId(id))!.AsCheckBox();
            if (box.IsChecked == true) box.Toggle();
        }
        ButtonByName(dialog, "Revert selected").Invoke();
    }

    private Button MainButton(string automationId) => session.MainElement(automationId).AsButton();

    private static Button ButtonByName(Window window, string name) =>
        window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)))?.AsButton()
        ?? throw new InvalidOperationException($"Button '{name}' was not found in '{window.Title}'.");
}
