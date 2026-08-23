// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

/// <summary>Stable RGB effect identifiers stored in configuration.</summary>
public static class RgbLightingEffectIds
{
    public const string Constant = "Constant";
    public const string Fade = "Fade";
    public const string Blink = "Blink";
    // Older settings used "Pulse" for the on/off effect now called Blink.
    public const string LegacyBlink = "Pulse";
}

public static class RgbLightingEffectDisplayNames
{
    public const string Pulse = "Pulse";
}

public static class RgbSettingJsonNames
{
    // Retained so existing settings and profile documents deserialize unchanged.
    public const string LegacyEffectSpeedMilliseconds = "PulseSpeedMilliseconds";
}

public static class OpenRgbMessages
{
    public const string SdkServerUnavailable = "OpenRGB's SDK server is unavailable.";

    public static string Unavailable(string detail) => $"OpenRGB unavailable: {detail}";
    public static string CouldNotStart(string detail) => $"Could not start OpenRGB: {detail}";
}
