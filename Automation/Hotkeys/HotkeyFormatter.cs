// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public enum HotkeyTrigger
{
    Keyboard,
    MiddleMouse,
    Mouse4,
    Mouse5,
    WheelUp,
    WheelDown,
    WheelLeft,
    WheelRight
}

internal static class HotkeyFormatter
{
    internal static string Format(int virtualKey, uint modifiers, HotkeyTrigger trigger = HotkeyTrigger.Keyboard)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(TriggerLabel(virtualKey, trigger));
        return string.Join(" + ", parts);
    }

    internal static string TriggerLabel(int virtualKey, HotkeyTrigger trigger) => trigger switch
    {
        HotkeyTrigger.MiddleMouse => "Middle mouse",
        HotkeyTrigger.Mouse4 => "Mouse 4",
        HotkeyTrigger.Mouse5 => "Mouse 5",
        HotkeyTrigger.WheelUp => "Wheel up",
        HotkeyTrigger.WheelDown => "Wheel down",
        HotkeyTrigger.WheelLeft => "Wheel left",
        HotkeyTrigger.WheelRight => "Wheel right",
        _ when virtualKey >= 0x30 && virtualKey <= 0x39 => (virtualKey - 0x30).ToString(),
        _ => virtualKey == 0 ? "None" : System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey).ToString()
    };

    internal static bool IsConfigured(int virtualKey, HotkeyTrigger trigger) =>
        trigger != HotkeyTrigger.Keyboard || virtualKey > 0;
}
