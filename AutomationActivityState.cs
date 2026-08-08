namespace AutoClicker;

// Keep user-visible activity indicators aligned with the worker lifecycle rather than individual UI events.
internal static class AutomationActivityState
{
    internal static bool IsActive(bool simpleActionRunning, int activeProfileActions) =>
        simpleActionRunning || activeProfileActions > 0;
}
