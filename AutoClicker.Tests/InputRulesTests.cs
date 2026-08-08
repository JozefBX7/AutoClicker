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
    [DataRow(-1, 0)]
    [DataRow(0, 0)]
    [DataRow(2, 2)]
    [DataRow(100, 5)]
    public void NormalizeInputPulseMilliseconds_BoundsSyntheticPressDuration(int value, int expected) =>
        Assert.AreEqual(expected, InputRules.NormalizeInputPulseMilliseconds(value));

    [TestMethod]
    public void DefaultInputPulseMilliseconds_IsFastAndWithinTheSupportedRange() =>
        Assert.AreEqual(3, InputRules.DefaultInputPulseMilliseconds);

    [TestMethod]
    public void CreateJitterMaximum_UsesSecondsAndMillisecondsAndClampsToOneMinute()
    {
        Assert.AreEqual(12_345, InputRules.CreateJitterMaximum(12, 345));
        Assert.AreEqual(InputRules.MaximumJitterMilliseconds, InputRules.CreateJitterMaximum(99, 9999));
    }

    [TestMethod]
    public void DescribeJitter_RoundTripsMaximumDelay()
    {
        var parts = InputRules.DescribeJitter(12_345);

        Assert.AreEqual(new InputRules.JitterParts(12, 345), parts);
    }

    [TestMethod]
    public void NextJitterOffsetMilliseconds_ReturnsAValueWithinTheSymmetricConfiguredRange()
    {
        var random = new Random(42);

        Assert.AreEqual(0, InputRules.NextJitterOffsetMilliseconds(0, random));
        for (var index = 0; index < 100; index++)
        {
            var offset = InputRules.NextJitterOffsetMilliseconds(10, random);
            Assert.IsTrue(offset >= -10);
            Assert.IsTrue(offset <= 10);
        }
    }

    [TestMethod]
    public void Jitter_ZeroOrNegativeMaximumIsDisabled()
    {
        var random = new Random(42);

        Assert.AreEqual(0, InputRules.CreateJitterMaximum(-1, -1));
        Assert.AreEqual(0, InputRules.NextJitterOffsetMilliseconds(0, random));
        Assert.AreEqual(0, InputRules.NextJitterOffsetMilliseconds(-1, random));
    }

    [TestMethod]
    public void ApplyJitter_VariesBothSidesOfTheBaseIntervalAndKeepsOneMillisecondMinimum()
    {
        Assert.AreEqual(60, InputRules.ApplyJitter(100, -40));
        Assert.AreEqual(140, InputRules.ApplyJitter(100, 40));
        Assert.AreEqual(1, InputRules.ApplyJitter(10, -40));
    }

    [TestMethod]
    public void NormalizeInterval_CarriesOverflowIntoLargerUnits()
    {
        Assert.AreEqual(new InputRules.IntervalParts(0, 0, 1, 0), InputRules.NormalizeInterval(0, 0, 0, 1000));
        Assert.AreEqual(new InputRules.IntervalParts(1, 1, 1, 1), InputRules.NormalizeInterval(0, 60, 60, 1001));
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
    [DataRow("Unset", 0, 0, false)]
    [DataRow("Left", 0, 0, true)]
    [DataRow("Custom", 0, 0, false)]
    [DataRow("Custom", 0x41, 0, true)]
    [DataRow("Sequence", 0, 1, false)]
    [DataRow("Sequence", 0, 2, true)]
    public void IsConfiguredAction_RequiresACompleteAction(string action, int customVirtualKey, int sequenceSteps, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsConfiguredAction(action, customVirtualKey, sequenceSteps));

    [DataTestMethod]
    [DataRow("Hold", true)]
    [DataRow("Single", false)]
    [DataRow("Double", false)]
    [DataRow(null, false)]
    public void IsHoldAction_IdentifiesHoldMode(string? actionType, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsHoldAction(actionType));

    [DataTestMethod]
    [DataRow("Left", 0, "Left click")]
    [DataRow("Right", 0, "Right click")]
    [DataRow("Middle", 0, "Middle click")]
    [DataRow("Space", 0, "Space")]
    [DataRow("Enter", 0, "Enter")]
    [DataRow("Sequence", 0, "Custom sequence")]
    [DataRow("Unknown", 0, "Set action")]
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

    [TestMethod]
    public void DescribeAction_UsesNumericLabelsForCustomTopRowDigits() =>
        Assert.AreEqual("9", InputRules.DescribeAction("Custom", 0x39));
}
