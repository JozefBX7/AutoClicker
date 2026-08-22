// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class MainWindowMenuStateTests
{
    [TestMethod]
    public void SharedMenuState_ReturnsCheckmarkForAllTrue() =>
        Assert.AreEqual("✓  ", MainWindow.SharedMenuState(new bool?[] { true, true }));

    [TestMethod]
    public void SharedMenuState_ReturnsTildeForMixedValues() =>
        Assert.AreEqual("~  ", MainWindow.SharedMenuState(new bool?[] { true, false }));

    [TestMethod]
    public void SharedMenuState_ReturnsTildeWhenAnyNull() =>
        Assert.AreEqual("~  ", MainWindow.SharedMenuState(new bool?[] { null, false }));

    [TestMethod]
    public void SharedMenuState_ReturnsEmptyForAllFalse() =>
        Assert.AreEqual(string.Empty, MainWindow.SharedMenuState(new bool?[] { false, false }));
}
