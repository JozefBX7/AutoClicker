// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AutomationActivityStateTests
{
    [DataTestMethod]
    [DataRow(false, 0, false)]
    [DataRow(true, 0, true)]
    [DataRow(false, 1, true)]
    [DataRow(false, 2, true)]
    [DataRow(true, 3, true)]
    public void IsActive_ReflectsEverySimpleAndProfileWorker(bool simpleActionRunning, int activeProfileActions, bool expected) =>
        Assert.AreEqual(expected, AutomationActivityState.IsActive(simpleActionRunning, activeProfileActions));

    [DataTestMethod]
    [DataRow(false, 0, false)]
    [DataRow(true, 0, true)]
    [DataRow(false, 1, true)]
    [DataRow(true, 2, true)]
    public void TaskbarPresentation_ShowsTheBadgeAndProgressOnlyWhileAnyAutomationIsActive(bool simpleActionRunning, int activeProfileActions, bool expected)
    {
        var presentation = AutomationActivityState.GetTaskbarPresentation(simpleActionRunning, activeProfileActions);

        Assert.AreEqual(expected, presentation.IsActive);
        Assert.AreEqual(expected, presentation.ShowActiveBadge);
        Assert.AreEqual(expected, presentation.ShowIndeterminateProgress);
    }
}
