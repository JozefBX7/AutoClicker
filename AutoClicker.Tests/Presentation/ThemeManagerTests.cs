// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class ThemeManagerTests
{
    [DataTestMethod]
    [DataRow(1, 1)]
    [DataRow(0, 0)]
    [DataRow(-1, 1)]
    public void ThemeFromAppsUseLightTheme_MapsWindowsPreference(int appsUseLightTheme, int expected) =>
        Assert.AreEqual((AppTheme)expected, ThemeManager.ThemeFromAppsUseLightTheme(appsUseLightTheme));

    [TestMethod]
    public void ThemeFromAppsUseLightTheme_FallsBackToDarkForMissingOrUnexpectedValues()
    {
        Assert.AreEqual(AppTheme.Dark, ThemeManager.ThemeFromAppsUseLightTheme(null));
        Assert.AreEqual(AppTheme.Dark, ThemeManager.ThemeFromAppsUseLightTheme("1"));
    }
}
