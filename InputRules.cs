namespace AutoClicker;

internal static class InputRules
{
    internal static int ParseClamped(string? value, int minimum, int maximum) => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : minimum;

    internal static TimeSpan CreateInterval(int hours, int minutes, int seconds, int milliseconds) =>
        TimeSpan.FromHours(Math.Clamp(hours, 0, 999))
        + TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 59))
        + TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 59))
        + TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 1, 999));

    internal static bool IsKeyboardAction(string? action) => action is "Space" or "Enter" or "Custom";

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
