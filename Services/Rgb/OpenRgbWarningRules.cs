// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class OpenRgbWarningRules
{
    internal static bool ShouldDisplay(bool lightingEnabled, bool applicationClosing) =>
        lightingEnabled && !applicationClosing;
}
