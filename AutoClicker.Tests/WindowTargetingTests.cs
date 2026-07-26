using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class WindowTargetingTests
{
    [TestMethod]
    public void ExecutableOnlyRule_MatchesAnyWindowFromThatExecutable()
    {
        var rule = new TargetWindowRule("notepad.exe", null);

        Assert.IsTrue(rule.Matches("NOTEPAD.EXE", "Untitled - Notepad"));
        Assert.IsFalse(rule.Matches("calc.exe", "Calculator"));
    }

    [TestMethod]
    public void PickedWindowRule_RequiresTheSelectedWindowTitle()
    {
        var rule = new TargetWindowRule("notepad.exe", "Notes - Notepad");

        Assert.IsTrue(rule.Matches("notepad.exe", "Notes - Notepad"));
        Assert.IsFalse(rule.Matches("notepad.exe", "Other - Notepad"));
    }

    [TestMethod]
    public void Rule_AcceptsAnExecutablePath()
    {
        var rule = new TargetWindowRule(@"C:\Windows\System32\notepad.exe", null);

        Assert.IsTrue(rule.Matches("notepad.exe", "Untitled - Notepad"));
    }

    [TestMethod]
    public void EmptyRule_IsDisabledAndAllowsGlobalInput()
    {
        var rule = new TargetWindowRule(string.Empty, null);

        Assert.IsFalse(rule.IsEnabled);
    }
}
