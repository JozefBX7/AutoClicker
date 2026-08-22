// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class WorkerSafetyTests
{
    [TestMethod]
    public void IsGuiHeartbeatExpired_AllowsHeartbeatInsideTheFiveSecondWindow()
    {
        const long frequency = 10_000;
        Assert.IsFalse(WorkerSafety.IsGuiHeartbeatExpired(100_000, 149_999, frequency));
    }

    [TestMethod]
    public void IsGuiHeartbeatExpired_StopsAtTheFiveSecondDeadline()
    {
        const long frequency = 10_000;
        Assert.IsTrue(WorkerSafety.IsGuiHeartbeatExpired(100_000, 150_000, frequency));
    }

    [TestMethod]
    public void IsGuiHeartbeatExpired_TreatsAnUnsetHeartbeatAsUnsafe() =>
        Assert.IsTrue(WorkerSafety.IsGuiHeartbeatExpired(0, 100_000, 10_000));
}
