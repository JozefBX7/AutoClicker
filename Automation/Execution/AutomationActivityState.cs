// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

// Keep user-visible activity indicators aligned with the worker lifecycle rather than individual UI events.
internal static class AutomationActivityState
{
    internal static bool IsActive(bool simpleActionRunning, int activeProfileActions) =>
        simpleActionRunning || activeProfileActions > 0;

    internal static TaskbarActivityPresentation GetTaskbarPresentation(bool simpleActionRunning, int activeProfileActions) =>
        new(IsActive(simpleActionRunning, activeProfileActions));
}

internal readonly record struct TaskbarActivityPresentation(bool IsActive)
{
    internal bool ShowActiveBadge => IsActive;
    internal bool ShowIndeterminateProgress => IsActive;
}
