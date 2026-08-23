// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class WindowPlacementRulesTests
{
    private static readonly WindowWorkArea Primary = new(0, 0, 1920, 1040);

    [TestMethod]
    public void RestoreToVisibleWorkArea_PreservesAnAlreadyVisiblePosition()
    {
        var restored = WindowPlacementRules.RestoreToVisibleWorkArea(new WindowPixelBounds(300, 200, 522, 580), [Primary]);

        Assert.AreEqual(new WindowPixelPosition(300, 200), restored);
    }

    [TestMethod]
    public void RestoreToVisibleWorkArea_ClampsEveryEdgeInsideTheNearestCurrentMonitor()
    {
        WindowWorkArea secondary = new(-1280, 40, 1280, 984);

        Assert.AreEqual(
            new WindowPixelPosition(-522, 444),
            WindowPlacementRules.RestoreToVisibleWorkArea(new WindowPixelBounds(-400, 900, 522, 580), [secondary, Primary]));
    }

    [TestMethod]
    public void RestoreToVisibleWorkArea_RecoversFromADisconnectedMonitor()
    {
        var restored = WindowPlacementRules.RestoreToVisibleWorkArea(new WindowPixelBounds(4000, 250, 522, 580), [Primary]);

        Assert.AreEqual(new WindowPixelPosition(1398, 250), restored);
    }

    [TestMethod]
    public void Clamp_AlignsAnOversizedWindowToTheWorkAreaOrigin()
    {
        Assert.AreEqual(
            new WindowPixelPosition(100, 50),
            WindowPlacementRules.Clamp(new WindowPixelBounds(800, 600, 1200, 900), new WindowWorkArea(100, 50, 800, 600)));
    }
}
