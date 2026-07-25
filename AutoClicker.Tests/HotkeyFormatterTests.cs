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
}
