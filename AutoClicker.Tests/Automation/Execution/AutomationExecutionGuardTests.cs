// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AutomationExecutionGuardTests
{
    [TestMethod]
    public void CanExecute_AllowsAnEnabledIdleMainContext() =>
        Assert.IsTrue(AutomationExecutionGuard.CanExecute(true, false, false, false, false));

    [DataTestMethod]
    [DataRow(false, false, false, false, false, DisplayName = "Modal owner disabled")]
    [DataRow(true, true, false, false, false, DisplayName = "App closing")]
    [DataRow(true, false, true, false, false, DisplayName = "Settings open")]
    [DataRow(true, false, false, true, false, DisplayName = "Hotkey capture")]
    [DataRow(true, false, false, false, true, DisplayName = "Input-key capture")]
    public void CanExecute_BlocksEveryNonRunningContext(
        bool ownerEnabled,
        bool isClosing,
        bool settingsOpen,
        bool capturingHotkey,
        bool capturingInputKey) =>
        Assert.IsFalse(AutomationExecutionGuard.CanExecute(ownerEnabled, isClosing, settingsOpen, capturingHotkey, capturingInputKey));
}
