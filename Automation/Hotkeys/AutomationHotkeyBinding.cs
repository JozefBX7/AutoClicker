// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

/// <summary>A reusable keyboard or mouse hotkey stored independently from an action's run settings.</summary>
public sealed class AutomationHotkeyBinding
{
    public int VirtualKey { get; set; }
    public uint Modifiers { get; set; }
    public HotkeyTrigger Trigger { get; set; } = HotkeyTrigger.Keyboard;

    public bool IsConfigured => HotkeyFormatter.IsConfigured(VirtualKey, Trigger);
    public AutomationHotkeyBinding Clone() => new() { VirtualKey = VirtualKey, Modifiers = Modifiers, Trigger = Trigger };
    public override string ToString() => HotkeyFormatter.Format(VirtualKey, Modifiers, Trigger);
}

internal enum AutomationHotkeyAssignmentKind
{
    RunAction,
    ToggleEnabled
}

internal static class AutomationHotkeyBindingRules
{
    private const uint AltModifier = 0x1;

    internal static AutomationHotkeyBinding RunBinding(AutomationAction action) => new()
    {
        VirtualKey = action.Settings.Hotkey,
        Modifiers = action.Settings.HotkeyModifiers,
        Trigger = action.Settings.HotkeyTrigger
    };

    internal static bool Conflicts(AutomationHotkeyBinding? left, AutomationHotkeyBinding? right, bool keyboardModifiersEnabled)
    {
        if (left?.IsConfigured != true || right?.IsConfigured != true || left.Trigger != right.Trigger) return false;
        if (left.Trigger != HotkeyTrigger.Keyboard) return left.Modifiers == right.Modifiers;
        return left.VirtualKey == right.VirtualKey
            && NormalizeKeyboardModifiers(left.Modifiers, keyboardModifiersEnabled) == NormalizeKeyboardModifiers(right.Modifiers, keyboardModifiersEnabled);
    }

    internal static bool IsAssigned(
        AutomationProfile profile,
        AutomationHotkeyBinding candidate,
        bool keyboardModifiersEnabled,
        string excludedActionId,
        AutomationHotkeyAssignmentKind excludedKind)
    {
        foreach (var action in profile.Actions)
        {
            if ((action.Id != excludedActionId || excludedKind != AutomationHotkeyAssignmentKind.RunAction)
                && Conflicts(candidate, RunBinding(action), keyboardModifiersEnabled)) return true;
            if ((action.Id != excludedActionId || excludedKind != AutomationHotkeyAssignmentKind.ToggleEnabled)
                && Conflicts(candidate, action.EnableToggleHotkey, keyboardModifiersEnabled)) return true;
        }
        return false;
    }

    internal static bool ActionsConflict(AutomationAction left, AutomationAction right, bool keyboardModifiersEnabled)
    {
        var leftBindings = Bindings(left);
        var rightBindings = Bindings(right);
        return leftBindings.Any(leftBinding => rightBindings.Any(rightBinding => Conflicts(leftBinding, rightBinding, keyboardModifiersEnabled)));
    }

    internal static bool ShouldActivateWhileHeldOnEnable(AutomationAction action, Func<AutomationHotkeyBinding, bool> isTriggerDown)
    {
        var binding = RunBinding(action);
        return action.HotkeyEnabled
            && InputRules.IsWhileHeldAction(action.Settings.ClickType)
            && binding.IsConfigured
            && isTriggerDown(binding);
    }

    internal static bool ActionEmitsOwnKeyboardBinding(AutomationAction action, AppDefaults effectiveSettings)
    {
        var input = string.IsNullOrWhiteSpace(effectiveSettings.Input) ? effectiveSettings.MouseButton : effectiveSettings.Input;
        return Bindings(action)
            .Where(binding => binding.Trigger == HotkeyTrigger.Keyboard)
            .Any(binding => InputRules.ActionUsesVirtualKey(input, effectiveSettings.CustomKey, effectiveSettings.CustomSequence, binding.VirtualKey));
    }

    private static IEnumerable<AutomationHotkeyBinding> Bindings(AutomationAction action)
    {
        var runBinding = RunBinding(action);
        if (runBinding.IsConfigured) yield return runBinding;
        if (action.EnableToggleHotkey?.IsConfigured == true) yield return action.EnableToggleHotkey;
    }

    private static uint NormalizeKeyboardModifiers(uint modifiers, bool keyboardModifiersEnabled) =>
        keyboardModifiersEnabled ? modifiers : modifiers & AltModifier;
}
