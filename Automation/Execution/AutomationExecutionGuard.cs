// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class AutomationExecutionGuard
{
    internal static bool CanExecute(
        bool ownerEnabled,
        bool isClosing,
        bool settingsOpen,
        bool capturingHotkey,
        bool capturingInputKey) =>
        ownerEnabled
        && !isClosing
        && !settingsOpen
        && !capturingHotkey
        && !capturingInputKey;
}
