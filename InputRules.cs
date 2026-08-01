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

    internal static bool IsKeyboardAction(string? action) => action is "Space" or "Enter" or "Custom";

    internal static bool IsConfiguredAction(string? action, int customVirtualKey, int sequenceStepCount) => action switch
    {
        "Left" or "Right" or "Middle" or "Space" or "Enter" => true,
        "Custom" => customVirtualKey != 0,
        "Sequence" => sequenceStepCount >= 2,
        _ => false
    };

    internal static bool IsHoldAction(string? actionType) => string.Equals(actionType, "Hold", StringComparison.Ordinal);

    internal static string DescribeAction(string? action, int customVirtualKey) => action switch
    {
        "Left" => "Left click",
        "Right" => "Right click",
        "Middle" => "Middle click",
        "Space" => "Space",
        "Enter" => "Enter",
        "Custom" when customVirtualKey != 0 => DescribeVirtualKey(customVirtualKey),
        "Sequence" => "Custom sequence",
        _ => "Set action"
    };

    private static string DescribeVirtualKey(int virtualKey) => System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey) switch
    {
        System.Windows.Input.Key.Return => "Enter",
        System.Windows.Input.Key.Space => "Space",
        var key => key.ToString()
    };
}
