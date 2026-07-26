namespace AutoClicker;

internal static class InputRules
{
    internal readonly record struct IntervalParts(int Hours, int Minutes, int Seconds, int Milliseconds);

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

    internal static bool IsKeyboardAction(string? action) => action is "Space" or "Enter" or "Custom";

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
        _ => "Selected action"
    };

    private static string DescribeVirtualKey(int virtualKey) => System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey) switch
    {
        System.Windows.Input.Key.Return => "Enter",
        System.Windows.Input.Key.Space => "Space",
        var key => key.ToString()
    };
}
