namespace AutoClicker;

internal static class OpenRgbWarningRules
{
    internal static bool ShouldDisplay(bool lightingEnabled, bool applicationClosing) =>
        lightingEnabled && !applicationClosing;
}
