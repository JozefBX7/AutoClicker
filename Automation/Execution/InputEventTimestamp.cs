namespace AutoClicker;

internal static class InputEventTimestamp
{
    internal static TimeSpan Elapsed(int previousTimestamp, int currentTimestamp) =>
        TimeSpan.FromMilliseconds(unchecked((uint)(currentTimestamp - previousTimestamp)));
}
