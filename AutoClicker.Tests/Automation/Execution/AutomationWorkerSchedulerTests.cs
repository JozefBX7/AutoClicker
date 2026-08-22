// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AutomationWorkerSchedulerTests
{
    [TestMethod]
    public async Task Start_UsesDedicatedThreadInsteadOfSharedThreadPool()
    {
        var runsOnThreadPool = true;

        await AutomationWorkerScheduler.Start(() => runsOnThreadPool = Thread.CurrentThread.IsThreadPoolThread);

        Assert.IsFalse(runsOnThreadPool);
    }
}
