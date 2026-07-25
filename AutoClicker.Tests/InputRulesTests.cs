using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class InputRulesTests
{
    [DataTestMethod]
    [DataRow("12", 0, 10, 10)]
    [DataRow("-2", 0, 10, 0)]
    [DataRow("invalid", 1, 9, 1)]
    [DataRow("7", 1, 9, 7)]
    public void ParseClamped_ConstrainsAndDefaultsValues(string text, int minimum, int maximum, int expected) =>
        Assert.AreEqual(expected, InputRules.ParseClamped(text, minimum, maximum));

    [TestMethod]
    public void CreateInterval_UsesAllUnits() =>
        Assert.AreEqual(TimeSpan.FromMilliseconds(3_723_004), InputRules.CreateInterval(1, 2, 3, 4));

    [TestMethod]
    public void CreateInterval_ClampsInvalidParts()
    {
        Assert.AreEqual(TimeSpan.FromHours(999) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59) + TimeSpan.FromMilliseconds(999), InputRules.CreateInterval(1000, 60, 60, 1000));
        Assert.AreEqual(TimeSpan.FromMilliseconds(1), InputRules.CreateInterval(-1, -1, -1, 0));
    }

    [DataTestMethod]
    [DataRow("Space", true)]
    [DataRow("Enter", true)]
    [DataRow("Custom", true)]
    [DataRow("Left", false)]
    [DataRow("Sequence", false)]
    [DataRow(null, false)]
    public void IsKeyboardAction_IdentifiesKeyboardInputs(string? action, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsKeyboardAction(action));

    [DataTestMethod]
    [DataRow("Left", 0, "Left click")]
    [DataRow("Right", 0, "Right click")]
    [DataRow("Middle", 0, "Middle click")]
    [DataRow("Space", 0, "Space")]
    [DataRow("Enter", 0, "Enter")]
    [DataRow("Sequence", 0, "Custom sequence")]
    [DataRow("Unknown", 0, "Selected action")]
    public void DescribeAction_ProducesConsistentLabels(string action, int virtualKey, string expected) =>
        Assert.AreEqual(expected, InputRules.DescribeAction(action, virtualKey));

    [TestMethod]
    public void DescribeAction_FormatsCustomVirtualKey() =>
        Assert.AreEqual("A", InputRules.DescribeAction("Custom", 0x41));

    [TestMethod]
    public void DescribeAction_UsesFriendlyNamesForCustomSpaceAndEnter()
    {
        Assert.AreEqual("Space", InputRules.DescribeAction("Custom", 0x20));
        Assert.AreEqual("Enter", InputRules.DescribeAction("Custom", 0x0D));
    }
}
