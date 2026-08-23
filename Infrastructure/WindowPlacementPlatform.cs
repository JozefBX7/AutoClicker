// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace AutoClicker;

internal static class WindowPlacementPlatform
{
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    internal static bool TryGetBounds(nint window, out WindowPixelBounds bounds)
    {
        bounds = default;
        if (window == 0 || !GetWindowRect(window, out var rectangle)) return false;
        bounds = new WindowPixelBounds(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    internal static bool Move(nint window, WindowPixelPosition position) =>
        window != 0 && SetWindowPos(window, 0, position.Left, position.Top, 0, 0, NoSize | NoZOrder | NoActivate);

    internal static IReadOnlyList<WindowWorkArea> CurrentWorkAreas() =>
        System.Windows.Forms.Screen.AllScreens
            .Select(screen => new WindowWorkArea(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height))
            .ToList();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRect { public int Left, Top, Right, Bottom; }

    [DllImport(NativeLibraryNames.User32)]
    private static extern bool GetWindowRect(nint window, out NativeWindowRect rectangle);

    [DllImport(NativeLibraryNames.User32)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
