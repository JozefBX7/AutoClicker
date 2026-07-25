namespace AutoClicker;

internal static class HotkeyFormatter
{
    internal static string Format(int virtualKey, uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey).ToString());
        return string.Join(" + ", parts);
    }
}
