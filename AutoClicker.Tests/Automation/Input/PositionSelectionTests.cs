using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class PositionSelectionTests
{
    [TestMethod]
    public void FromPickedPoint_UsesFixedPositionAndPreservesCoordinates()
    {
        var selection = PositionSelection.FromPickedPoint(-640, 1234);

        Assert.IsTrue(selection.FixedPosition);
        Assert.AreEqual(-640, selection.X);
        Assert.AreEqual(1234, selection.Y);
    }
}