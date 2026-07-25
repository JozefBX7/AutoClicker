using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceStepTests
{
    [DataTestMethod]
    [DataRow("Left", "Left click")]
    [DataRow("Right", "Right click")]
    [DataRow("Middle", "Middle click")]
    [DataRow("Space", "Space")]
    [DataRow("Enter", "Enter")]
    [DataRow("Something else", "Something else")]
    public void Describe_UsesFriendlyKnownActionNames(string input, string expected) =>
        Assert.AreEqual(expected, new SequenceStep { Input = input }.Describe());

    [TestMethod]
    public void Clone_CopiesValuesWithoutSharingTheObject()
    {
        var original = new SequenceStep { Input = "Custom", CustomKey = 0x41, DelayAfterMilliseconds = 80 };
        var clone = original.Clone();

        Assert.AreNotSame(original, clone);
        Assert.AreEqual(original.Input, clone.Input);
        Assert.AreEqual(original.CustomKey, clone.CustomKey);
        Assert.AreEqual(original.DelayAfterMilliseconds, clone.DelayAfterMilliseconds);
    }

    [TestMethod]
    public void ToString_DescribesTheActionAndDelay()
    {
        var text = new SequenceStep { Input = "Left", DelayAfterMilliseconds = 125 }.ToString();
        StringAssert.Contains(text, "Left click");
        StringAssert.Contains(text, "125 ms");
    }

    [TestMethod]
    public void ToString_DescribesAnExplicitDelayEvent()
    {
        var step = new SequenceStep { Input = "Delay", DelayAfterMilliseconds = 180 };

        Assert.AreEqual("Wait", step.Describe());
        Assert.AreEqual("Wait 180 ms", step.ToString());
    }

    [TestMethod]
    public void Describe_FormatsCustomVirtualKeys() =>
        Assert.AreEqual("A", new SequenceStep { Input = "Custom", CustomKey = 0x41 }.Describe());
}
