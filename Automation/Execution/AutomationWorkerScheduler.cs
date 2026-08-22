// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

// Timed automation loops wait for most of their lifetime, so they should not occupy the shared .NET thread pool.
internal static class AutomationWorkerScheduler
{
    internal static Task Start(Action work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default);
}
