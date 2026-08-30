// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceStepTests
{
    [DataTestMethod]
    [DataRow(AutomationInputIds.Left, AutomationInputLabels.LeftClick)]
    [DataRow(AutomationInputIds.Right, AutomationInputLabels.RightClick)]
    [DataRow(AutomationInputIds.Middle, AutomationInputLabels.MiddleClick)]
    [DataRow(AutomationInputIds.Mouse4, AutomationInputLabels.Mouse4Click)]
    [DataRow(AutomationInputIds.Mouse5, AutomationInputLabels.Mouse5Click)]
    [DataRow(AutomationInputIds.ScrollUp, AutomationInputLabels.ScrollUp)]
    [DataRow(AutomationInputIds.ScrollDown, AutomationInputLabels.ScrollDown)]
    [DataRow(AutomationInputIds.ScrollLeft, AutomationInputLabels.ScrollLeft)]
    [DataRow(AutomationInputIds.ScrollRight, AutomationInputLabels.ScrollRight)]
    [DataRow(AutomationInputIds.Space, AutomationInputIds.Space)]
    [DataRow(AutomationInputIds.Enter, AutomationInputIds.Enter)]
    [DataRow("Something else", "Something else")]
    public void Describe_UsesFriendlyKnownActionNames(string input, string expected) =>
        Assert.AreEqual(expected, new SequenceStep { Input = input }.Describe());

    [TestMethod]
    public void Clone_CopiesValuesWithoutSharingTheObject()
    {
        var original = new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x41, DelayAfterMilliseconds = 80, Mode = SequenceStepMode.Hold };
        var clone = original.Clone();

        Assert.AreNotSame(original, clone);
        Assert.AreEqual(original.Input, clone.Input);
        Assert.AreEqual(original.CustomKey, clone.CustomKey);
        Assert.AreEqual(original.DelayAfterMilliseconds, clone.DelayAfterMilliseconds);
        Assert.AreEqual(SequenceStepMode.Hold, clone.Mode);
    }

    [TestMethod]
    public void PresetClone_PreservesGlobalPulseOverride()
    {
        var original = new SequencePreset { Name = "Instant", UseGlobalInputPulse = false, Steps = [new SequenceStep { Input = AutomationInputIds.Left }, new SequenceStep { Input = AutomationInputIds.Right }] };
        var clone = original.Clone();

        Assert.IsFalse(clone.UseGlobalInputPulse);
    }

    [TestMethod]
    public void ToString_DescribesTheActionAndDelay()
    {
        var text = new SequenceStep { Input = AutomationInputIds.Left, DelayAfterMilliseconds = 125 }.ToString();
        StringAssert.Contains(text, AutomationInputLabels.LeftClick);
        StringAssert.Contains(text, "125 ms");
    }

    [TestMethod]
    public void ToString_DescribesAnExplicitDelayEvent()
    {
        var step = new SequenceStep { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = 180 };

        Assert.AreEqual("Wait", step.Describe());
        Assert.AreEqual("Wait 180 ms", step.ToString());
    }

    [TestMethod]
    public void Describe_FormatsCustomVirtualKeys() =>
        Assert.AreEqual("A", new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x41 }.Describe());

    [DataTestMethod]
    [DataRow(SequenceStepMode.Hold, "Hold left mouse")]
    [DataRow(SequenceStepMode.Release, "Release left mouse")]
    public void ToString_DescribesStatefulMouseEvents(SequenceStepMode mode, string expected) =>
        Assert.AreEqual(expected, new SequenceStep { Input = AutomationInputIds.Left, Mode = mode }.ToString());
}
