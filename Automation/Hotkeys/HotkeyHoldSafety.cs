// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class HotkeyHoldSafety
{
    private const uint Alt = 0x1;
    private const uint Control = 0x2;
    private const uint Shift = 0x4;

    internal static int? PhysicalVirtualKey(HotkeyTrigger trigger, int keyboardVirtualKey) => trigger switch
    {
        HotkeyTrigger.Keyboard when keyboardVirtualKey > 0 => keyboardVirtualKey,
        HotkeyTrigger.MiddleMouse => 0x04,
        HotkeyTrigger.Mouse4 => 0x05,
        HotkeyTrigger.Mouse5 => 0x06,
        _ => null
    };

    internal static uint RequiredKeyboardModifiers(bool modifiersEnabled, uint configuredModifiers) =>
        modifiersEnabled ? configuredModifiers & (Alt | Control | Shift) : configuredModifiers & Alt;

    internal static bool IsTriggerDown(int virtualKey, uint requiredModifiers, Func<int, bool> isKeyDown) =>
        isKeyDown(virtualKey)
        && ((requiredModifiers & Alt) == 0 || isKeyDown(0x12))
        && ((requiredModifiers & Control) == 0 || isKeyDown(0x11))
        && ((requiredModifiers & Shift) == 0 || isKeyDown(0x10));
}
