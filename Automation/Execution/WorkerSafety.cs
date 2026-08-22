// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class WorkerSafety
{
    internal const int GuiHeartbeatTimeoutSeconds = 5;

    internal static bool IsGuiHeartbeatExpired(long lastHeartbeatTimestamp, long currentTimestamp, long stopwatchFrequency) =>
        lastHeartbeatTimestamp <= 0 || currentTimestamp - lastHeartbeatTimestamp >= stopwatchFrequency * GuiHeartbeatTimeoutSeconds;
}
