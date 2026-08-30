// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class InputRules
{
    internal const int DefaultInputPulseMilliseconds = 3;
    internal const int MaximumJitterMilliseconds = 59_999;
    internal readonly record struct IntervalParts(int Hours, int Minutes, int Seconds, int Milliseconds);
    internal readonly record struct JitterParts(int Seconds, int Milliseconds);

    internal static int ParseClamped(string? value, int minimum, int maximum) => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : minimum;

    internal static IntervalParts NormalizeInterval(long hours, long minutes, long seconds, long milliseconds)
    {
        const long maximumMilliseconds = (((999L * 60) + 59) * 60 + 59) * 1000 + 999;
        var totalMilliseconds = Math.Max(0, hours) * 3_600_000L
            + Math.Max(0, minutes) * 60_000L
            + Math.Max(0, seconds) * 1_000L
            + Math.Max(0, milliseconds);
        totalMilliseconds = Math.Clamp(totalMilliseconds, 1, maximumMilliseconds);

        var normalizedHours = (int)(totalMilliseconds / 3_600_000L);
        totalMilliseconds %= 3_600_000L;
        var normalizedMinutes = (int)(totalMilliseconds / 60_000L);
        totalMilliseconds %= 60_000L;
        var normalizedSeconds = (int)(totalMilliseconds / 1_000L);
        return new IntervalParts(normalizedHours, normalizedMinutes, normalizedSeconds, (int)(totalMilliseconds % 1_000L));
    }

    internal static TimeSpan CreateInterval(int hours, int minutes, int seconds, int milliseconds)
    {
        var parts = NormalizeInterval(hours, minutes, seconds, milliseconds);
        return TimeSpan.FromHours(parts.Hours) + TimeSpan.FromMinutes(parts.Minutes) + TimeSpan.FromSeconds(parts.Seconds) + TimeSpan.FromMilliseconds(parts.Milliseconds);
    }

    internal static int NormalizeInputPulseMilliseconds(int milliseconds) => Math.Clamp(milliseconds, 0, 5);

    internal static long CreateJitterMaximum(long seconds, long milliseconds)
    {
        var total = Math.Max(0, seconds) * 1_000L
            + Math.Max(0, milliseconds);
        return Math.Clamp(total, 0, MaximumJitterMilliseconds);
    }

    internal static JitterParts DescribeJitter(long milliseconds)
    {
        var total = Math.Clamp(milliseconds, 0, MaximumJitterMilliseconds);
        var seconds = (int)(total / 1_000L);
        return new JitterParts(seconds, (int)(total % 1_000L));
    }

    internal static long NextJitterOffsetMilliseconds(long maximumMilliseconds, Random random)
    {
        var maximum = Math.Clamp(maximumMilliseconds, 0, MaximumJitterMilliseconds);
        return maximum == 0 ? 0 : random.NextInt64(-maximum, maximum + 1);
    }

    internal static long ApplyJitter(long baseMilliseconds, long offsetMilliseconds) => Math.Max(1, baseMilliseconds + offsetMilliseconds);

    internal static bool IsKeyboardAction(string? action) => action is AutomationInputIds.Space or AutomationInputIds.Enter or AutomationInputIds.Custom;

    internal static bool IsInstantaneousMouseAction(string? action) => action is
        AutomationInputIds.ScrollUp or AutomationInputIds.ScrollDown or AutomationInputIds.ScrollLeft or AutomationInputIds.ScrollRight;

    internal static bool IsConfiguredAction(string? action, int customVirtualKey, int sequenceStepCount) => action switch
    {
        AutomationInputIds.Left or AutomationInputIds.Right or AutomationInputIds.Middle or AutomationInputIds.Mouse4 or AutomationInputIds.Mouse5
            or AutomationInputIds.ScrollUp or AutomationInputIds.ScrollDown or AutomationInputIds.ScrollLeft or AutomationInputIds.ScrollRight
            or AutomationInputIds.Space or AutomationInputIds.Enter => true,
        AutomationInputIds.Custom => customVirtualKey != 0,
        AutomationInputIds.Sequence => sequenceStepCount >= 2,
        _ => false
    };

    internal static bool IsHoldAction(string? actionType) => string.Equals(actionType, AutomationActionTypeIds.Hold, StringComparison.Ordinal);

    internal static bool IsWhileHeldAction(string? actionType) => string.Equals(actionType, AutomationActionTypeIds.WhileHeld, StringComparison.Ordinal);

    internal static bool RequiresContinuousRun(string? actionType) => IsHoldAction(actionType) || IsWhileHeldAction(actionType);

    internal static bool ActionUsesVirtualKey(string? action, int customVirtualKey, IEnumerable<SequenceStep>? sequence, int virtualKey) => action switch
    {
        AutomationInputIds.Space => virtualKey == 0x20,
        AutomationInputIds.Enter => virtualKey == 0x0D,
        AutomationInputIds.Custom => customVirtualKey == virtualKey,
        AutomationInputIds.Sequence => sequence?.Any(step => ActionUsesVirtualKey(step.Input, step.CustomKey, null, virtualKey)) == true,
        _ => false
    };

    internal static string DescribeAction(string? action, int customVirtualKey) => action switch
    {
        AutomationInputIds.Left => AutomationInputLabels.LeftClick,
        AutomationInputIds.Right => AutomationInputLabels.RightClick,
        AutomationInputIds.Middle => AutomationInputLabels.MiddleClick,
        AutomationInputIds.Mouse4 => AutomationInputLabels.Mouse4Click,
        AutomationInputIds.Mouse5 => AutomationInputLabels.Mouse5Click,
        AutomationInputIds.ScrollUp => AutomationInputLabels.ScrollUp,
        AutomationInputIds.ScrollDown => AutomationInputLabels.ScrollDown,
        AutomationInputIds.ScrollLeft => AutomationInputLabels.ScrollLeft,
        AutomationInputIds.ScrollRight => AutomationInputLabels.ScrollRight,
        AutomationInputIds.Space => AutomationInputIds.Space,
        AutomationInputIds.Enter => AutomationInputIds.Enter,
        AutomationInputIds.Custom when customVirtualKey != 0 => DescribeVirtualKey(customVirtualKey),
        AutomationInputIds.Sequence => AutomationInputLabels.CustomSequence,
        _ => AutomationInputLabels.SetAction
    };

    private static string DescribeVirtualKey(int virtualKey) => System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey) switch
    {
        var key when virtualKey >= 0x30 && virtualKey <= 0x39 => (virtualKey - 0x30).ToString(),
        System.Windows.Input.Key.Return => AutomationInputIds.Enter,
        System.Windows.Input.Key.Space => AutomationInputIds.Space,
        var key => key.ToString()
    };
}
