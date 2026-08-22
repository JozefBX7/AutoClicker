// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AutoClicker.E2E;

internal sealed class AdvancedActionRobot(AutoClickerE2ESession session)
{
    internal void AddHotkey(VirtualKeyShort key)
    {
        session.MainElement("AddHotkey").AsButton().Invoke();
        session.Window.Focus();
        Keyboard.Press(key);
    }

    internal void CancelAddingHotkey()
    {
        session.MainElement("AddHotkey").AsButton().Invoke();
        session.Window.Focus();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
    }

    internal void OpenMenu(string actionId)
    {
        session.Window.Focus();
        session.MainElement($"Action_{actionId}").AsButton().RightClick();
    }

    internal void ChooseMenu(string actionId, string automationId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            OpenMenu(actionId);
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
        throw new TimeoutException($"action menu item '{automationId}' was not found");
    }

    internal void ChangeHotkey(string actionId, VirtualKeyShort key)
    {
        ChooseMenu(actionId, "ChangeActionHotkey");
        session.Window.Focus();
        Keyboard.Press(key);
    }

    internal void SelectTogether(params string[] actionIds)
    {
        if (actionIds.Length == 0) return;
        session.MainElement($"Action_{actionIds[0]}").AsButton().Click();
        using var control = Keyboard.Pressing(VirtualKeyShort.CONTROL);
        foreach (var actionId in actionIds.Skip(1))
            session.MainElement($"Action_{actionId}").AsButton().Click();
    }

    internal void ConfigureLighting(string actionId, bool enabled, string effect)
    {
        ChooseMenu(actionId, "ConfigureActionLighting");
        var dialog = session.Dialog("Hotkey lighting");
        var enabledBox = dialog.FindFirstDescendant(condition => condition.ByAutomationId("EnabledCheckBox"))!.AsCheckBox();
        if ((enabledBox.IsChecked == true) != enabled) enabledBox.Toggle();
        var combo = dialog.FindFirstDescendant(condition => condition.ByAutomationId("EffectCombo"))!.AsComboBox();
        combo.Expand();
        combo.Items.Single(item => item.Name == effect).Select();
        Button(dialog, "Save override").Invoke();
    }

    internal void RevertAllBehavior(string actionId)
    {
        ChooseMenu(actionId, "ToggleActionSharedBehavior");
        var dialog = session.Dialog("Shared behavior defaults");
        Button(dialog, "Revert all").Invoke();
    }

    internal void CopyToNewProfile(string actionId, string profileName)
    {
        ChooseMenu(actionId, "CopyActionsToProfile");
        var dialog = session.Dialog("Copy hotkeys");
        dialog.FindFirstDescendant(condition => condition.ByAutomationId("NewProfileNameBox"))!.AsTextBox().Text = profileName;
        Button(dialog, "Copy").Invoke();
    }

    private static Button Button(Window window, string name) =>
        window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)))!.AsButton();
}
