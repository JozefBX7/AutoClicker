// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceHoldRulesTests
{
    [TestMethod]
    public void ValidationError_AcceptsBalancedOverlappingHoldsAndWaits()
    {
        SequenceStep[] sequence =
        [
            Key(0x11, SequenceStepMode.Hold),
            Key(0x41, SequenceStepMode.Hold),
            new() { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = 100 },
            Key(0x41, SequenceStepMode.Release),
            Key(0x11, SequenceStepMode.Release)
        ];

        Assert.IsNull(SequenceHoldRules.ValidationError(sequence));
        Assert.IsTrue(SequenceHoldRules.ContainsHold(sequence));
    }

    [TestMethod]
    public void ValidationError_RejectsReleaseBeforeHold() =>
        StringAssert.Contains(SequenceHoldRules.ValidationError([Key(0x41, SequenceStepMode.Release)])!, "matching Hold");

    [TestMethod]
    public void ValidationError_RejectsUnreleasedInput() =>
        StringAssert.Contains(SequenceHoldRules.ValidationError([Key(0x41, SequenceStepMode.Hold)])!, "Release");

    [DataTestMethod]
    [DataRow("Unknown", 0)]
    [DataRow(AutomationInputIds.Custom, 0)]
    [DataRow(AutomationInputIds.Custom, 0x100)]
    public void ValidationError_RejectsUnsupportedOrInvalidInputs(string input, int customKey) =>
        StringAssert.Contains(SequenceHoldRules.ValidationError([new SequenceStep { Input = input, CustomKey = customKey }])!, "not a supported");

    [TestMethod]
    public void ValidationError_RejectsNormalPressWhileTheSameInputIsHeld()
    {
        var error = SequenceHoldRules.ValidationError(
        [
            new SequenceStep { Input = AutomationInputIds.Left, Mode = SequenceStepMode.Hold },
            new SequenceStep { Input = AutomationInputIds.Left },
            new SequenceStep { Input = AutomationInputIds.Left, Mode = SequenceStepMode.Release }
        ]);

        StringAssert.Contains(error!, "still held");
    }

    [TestMethod]
    public void Identity_DistinguishesCustomKeysButNotUnusedKeyValues()
    {
        Assert.AreNotEqual(SequenceHoldRules.Identity(Key(0x41, SequenceStepMode.Hold)), SequenceHoldRules.Identity(Key(0x42, SequenceStepMode.Hold)));
        Assert.AreEqual(
            SequenceHoldRules.Identity(new SequenceStep { Input = AutomationInputIds.Left, CustomKey = 1 }),
            SequenceHoldRules.Identity(new SequenceStep { Input = AutomationInputIds.Left, CustomKey = 2 }));
    }

    private static SequenceStep Key(int virtualKey, SequenceStepMode mode) =>
        new() { Input = AutomationInputIds.Custom, CustomKey = virtualKey, Mode = mode };
}
