// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal readonly record struct WindowPixelBounds(int Left, int Top, int Width, int Height);
internal readonly record struct WindowWorkArea(int Left, int Top, int Width, int Height);
internal readonly record struct WindowPixelPosition(int Left, int Top);

internal static class WindowPlacementRules
{
    internal static WindowPixelPosition RestoreToVisibleWorkArea(
        WindowPixelBounds savedBounds,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        if (workAreas.Count == 0) return new WindowPixelPosition(savedBounds.Left, savedBounds.Top);

        var target = workAreas
            .OrderByDescending(area => IntersectionArea(savedBounds, area))
            .ThenBy(area => DistanceSquaredFromCenter(savedBounds, area))
            .First();
        return Clamp(savedBounds, target);
    }

    internal static WindowPixelPosition Clamp(WindowPixelBounds bounds, WindowWorkArea workArea)
    {
        var availableWidth = Math.Max(1, workArea.Width);
        var availableHeight = Math.Max(1, workArea.Height);
        var windowWidth = Math.Max(1, bounds.Width);
        var windowHeight = Math.Max(1, bounds.Height);
        var maximumLeft = workArea.Left + Math.Max(0, availableWidth - windowWidth);
        var maximumTop = workArea.Top + Math.Max(0, availableHeight - windowHeight);
        return new WindowPixelPosition(
            Math.Clamp(bounds.Left, workArea.Left, maximumLeft),
            Math.Clamp(bounds.Top, workArea.Top, maximumTop));
    }

    private static long IntersectionArea(WindowPixelBounds bounds, WindowWorkArea area)
    {
        var width = Math.Max(0, Math.Min((long)bounds.Left + bounds.Width, (long)area.Left + area.Width) - Math.Max(bounds.Left, area.Left));
        var height = Math.Max(0, Math.Min((long)bounds.Top + bounds.Height, (long)area.Top + area.Height) - Math.Max(bounds.Top, area.Top));
        return width * height;
    }

    private static long DistanceSquaredFromCenter(WindowPixelBounds bounds, WindowWorkArea area)
    {
        var centerX = (long)bounds.Left + bounds.Width / 2;
        var centerY = (long)bounds.Top + bounds.Height / 2;
        var nearestX = Math.Clamp(centerX, area.Left, (long)area.Left + Math.Max(1, area.Width));
        var nearestY = Math.Clamp(centerY, area.Top, (long)area.Top + Math.Max(1, area.Height));
        var deltaX = centerX - nearestX;
        var deltaY = centerY - nearestY;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
