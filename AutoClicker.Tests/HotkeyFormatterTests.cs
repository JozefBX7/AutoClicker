using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class HotkeyFormatterTests
{
    [DataTestMethod]
    [DataRow(117, 0u, "F6")]
    [DataRow(117, 2u, "Ctrl + F6")]
    [DataRow(117, 1u, "Alt + F6")]
    [DataRow(117, 4u, "Shift + F6")]
    [DataRow(117, 7u, "Ctrl + Alt + Shift + F6")]
    public void Format_UsesModifiersInTheUiOrder(int virtualKey, uint modifiers, string expected) =>
        Assert.AreEqual(expected, HotkeyFormatter.Format(virtualKey, modifiers));

    [TestMethod]
    public void Format_UsesClearLabelsForSupportedMouseTriggers()
    {
        Assert.AreEqual("Middle mouse", HotkeyFormatter.Format(0, 0, HotkeyTrigger.MiddleMouse));
        Assert.AreEqual("Ctrl + Mouse 4", HotkeyFormatter.Format(0, 2, HotkeyTrigger.Mouse4));
        Assert.AreEqual("Wheel down", HotkeyFormatter.Format(0, 0, HotkeyTrigger.WheelDown));
        Assert.AreEqual("Shift + Wheel right", HotkeyFormatter.Format(0, 4, HotkeyTrigger.WheelRight));
    }

    [TestMethod]
    public void IsConfigured_RecognizesMouseBindingsWithoutVirtualKeys()
    {
        Assert.IsFalse(HotkeyFormatter.IsConfigured(0, HotkeyTrigger.Keyboard));
        Assert.IsTrue(HotkeyFormatter.IsConfigured(0, HotkeyTrigger.Mouse5));
    }
}
