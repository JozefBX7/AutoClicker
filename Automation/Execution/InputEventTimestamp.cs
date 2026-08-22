// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class InputEventTimestamp
{
    internal static TimeSpan Elapsed(int previousTimestamp, int currentTimestamp) =>
        TimeSpan.FromMilliseconds(unchecked((uint)(currentTimestamp - previousTimestamp)));
}
