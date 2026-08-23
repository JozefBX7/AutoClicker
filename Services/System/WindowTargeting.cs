// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker;

internal sealed record TargetWindowRule(string ExecutableName, string? WindowTitle)
{
    internal bool IsEnabled => !string.IsNullOrWhiteSpace(ExecutableName);

    internal bool Matches(string executableName, string windowTitle) =>
        string.Equals(Path.GetFileName(ExecutableName.Trim()), Path.GetFileName(executableName), StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(WindowTitle) || string.Equals(WindowTitle, windowTitle, StringComparison.Ordinal));
}

public sealed record VisibleWindow(string ExecutableName, string Title)
{
    public string DisplayName => $"{Title} - {ExecutableName}";
}

internal static class WindowTargeting
{
    internal static bool IsForeground(TargetWindowRule rule)
    {
        if (!rule.IsEnabled) return true;
        var window = GetForegroundWindow();
        return window != 0 && TryGetWindowDetails(window, out var executableName, out var title) && rule.Matches(executableName, title);
    }

    internal static bool IsPointInForegroundClientArea(int x, int y)
    {
        var window = GetForegroundWindow();
        if (window == 0 || !GetClientRect(window, out var clientRect)) return false;
        var topLeft = new NativePoint { X = clientRect.Left, Y = clientRect.Top };
        var bottomRight = new NativePoint { X = clientRect.Right, Y = clientRect.Bottom };
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight)) return false;
        return x >= topLeft.X && x < bottomRight.X && y >= topLeft.Y && y < bottomRight.Y;
    }

    internal static IReadOnlyList<VisibleWindow> GetVisibleWindows()
    {
        var windows = new List<VisibleWindow>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0) return true;
            if (TryGetWindowDetails(window, out var executableName, out var title)) windows.Add(new VisibleWindow(executableName, title));
            return true;
        }, 0);
        return windows.OrderBy(window => window.ExecutableName, StringComparer.OrdinalIgnoreCase).ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetWindowDetails(nint window, out string executableName, out string title)
    {
        executableName = string.Empty;
        title = GetWindowTitle(window);
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || string.IsNullOrWhiteSpace(title)) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            executableName = Path.GetFileName(process.MainModule?.FileName ?? process.ProcessName);
            if (!executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executableName += ".exe";
            return !string.IsNullOrWhiteSpace(executableName);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static string GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length == 0) return string.Empty;
        var title = new StringBuilder(length + 1);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport(NativeLibraryNames.User32)] private static extern nint GetForegroundWindow();
    [DllImport(NativeLibraryNames.User32)] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport(NativeLibraryNames.User32)] private static extern bool IsWindowVisible(nint window);
    [DllImport(NativeLibraryNames.User32)] private static extern int GetWindowTextLength(nint window);
    [DllImport(NativeLibraryNames.User32, CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);
    [DllImport(NativeLibraryNames.User32)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport(NativeLibraryNames.User32)] private static extern bool GetClientRect(nint window, out NativeRect rectangle);
    [DllImport(NativeLibraryNames.User32)] private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
