// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

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
    [DataRow(AutomationInputIds.Space, true)]
    [DataRow(AutomationInputIds.Enter, true)]
    [DataRow(AutomationInputIds.Custom, true)]
    [DataRow(AutomationInputIds.Left, false)]
    [DataRow(AutomationInputIds.Sequence, false)]
    [DataRow(null, false)]
    public void IsKeyboardAction_IdentifiesKeyboardInputs(string? action, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsKeyboardAction(action));

    [DataTestMethod]
    [DataRow(AutomationInputIds.Unset, 0, 0, false)]
    [DataRow(AutomationInputIds.Left, 0, 0, true)]
    [DataRow(AutomationInputIds.Custom, 0, 0, false)]
    [DataRow(AutomationInputIds.Custom, 0x41, 0, true)]
    [DataRow(AutomationInputIds.Sequence, 0, 1, false)]
    [DataRow(AutomationInputIds.Sequence, 0, 2, true)]
    public void IsConfiguredAction_RequiresACompleteAction(string action, int customVirtualKey, int sequenceSteps, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsConfiguredAction(action, customVirtualKey, sequenceSteps));

    [DataTestMethod]
    [DataRow(AutomationActionTypeIds.Hold, true)]
    [DataRow(AutomationActionTypeIds.Single, false)]
    [DataRow(AutomationActionTypeIds.Double, false)]
    [DataRow(null, false)]
    public void IsHoldAction_IdentifiesHoldMode(string? actionType, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsHoldAction(actionType));

    [DataTestMethod]
    [DataRow(AutomationActionTypeIds.WhileHeld, true)]
    [DataRow(AutomationActionTypeIds.Hold, false)]
    [DataRow(AutomationActionTypeIds.Single, false)]
    [DataRow(null, false)]
    public void IsWhileHeldAction_IdentifiesHotkeyHoldMode(string? actionType, bool expected) =>
        Assert.AreEqual(expected, InputRules.IsWhileHeldAction(actionType));

    [DataTestMethod]
    [DataRow(AutomationActionTypeIds.Hold, true)]
    [DataRow(AutomationActionTypeIds.WhileHeld, true)]
    [DataRow(AutomationActionTypeIds.Single, false)]
    [DataRow(AutomationActionTypeIds.Double, false)]
    public void RequiresContinuousRun_DisablesFiniteRepeatForContinuousModes(string actionType, bool expected) =>
        Assert.AreEqual(expected, InputRules.RequiresContinuousRun(actionType));

    [TestMethod]
    public void ActionUsesVirtualKey_FindsDirectAndSequenceKeysWithoutMatchingMouseActions()
    {
        Assert.IsTrue(InputRules.ActionUsesVirtualKey(AutomationInputIds.Space, 0, null, 0x20));
        Assert.IsTrue(InputRules.ActionUsesVirtualKey(AutomationInputIds.Custom, 0x75, null, 0x75));
        Assert.IsTrue(InputRules.ActionUsesVirtualKey(AutomationInputIds.Sequence, 0,
            [new SequenceStep { Input = AutomationInputIds.Left }, new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x76 }], 0x76));
        Assert.IsFalse(InputRules.ActionUsesVirtualKey(AutomationInputIds.Sequence, 0,
            [new SequenceStep { Input = AutomationInputIds.Left }, new SequenceStep { Input = AutomationInputIds.Enter }], 0x76));
    }

    [DataTestMethod]
    [DataRow(AutomationInputIds.Left, 0, AutomationInputLabels.LeftClick)]
    [DataRow(AutomationInputIds.Right, 0, AutomationInputLabels.RightClick)]
    [DataRow(AutomationInputIds.Middle, 0, AutomationInputLabels.MiddleClick)]
    [DataRow(AutomationInputIds.Space, 0, AutomationInputIds.Space)]
    [DataRow(AutomationInputIds.Enter, 0, AutomationInputIds.Enter)]
    [DataRow(AutomationInputIds.Sequence, 0, AutomationInputLabels.CustomSequence)]
    [DataRow("Unknown", 0, AutomationInputLabels.SetAction)]
    public void DescribeAction_ProducesConsistentLabels(string action, int virtualKey, string expected) =>
        Assert.AreEqual(expected, InputRules.DescribeAction(action, virtualKey));

    [TestMethod]
    public void DescribeAction_FormatsCustomVirtualKey() =>
        Assert.AreEqual("A", InputRules.DescribeAction(AutomationInputIds.Custom, 0x41));

    [TestMethod]
    public void DescribeAction_UsesFriendlyNamesForCustomSpaceAndEnter()
    {
        Assert.AreEqual(AutomationInputIds.Space, InputRules.DescribeAction(AutomationInputIds.Custom, 0x20));
        Assert.AreEqual(AutomationInputIds.Enter, InputRules.DescribeAction(AutomationInputIds.Custom, 0x0D));
    }

    [TestMethod]
    public void DescribeAction_UsesNumericLabelsForCustomTopRowDigits() =>
        Assert.AreEqual("9", InputRules.DescribeAction(AutomationInputIds.Custom, 0x39));
}
