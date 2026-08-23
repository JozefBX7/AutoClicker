// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using OpenRGB.NET;

namespace AutoClicker;

/// <summary>
/// Classifies OpenRGB controllers without relying on vendor or product names.
/// OpenRGB's device type is authoritative. Generic metadata and the exposed LED
/// layout are used only when a controller reports its type as unknown.
/// </summary>
internal static class OpenRgbDeviceClassifier
{
    private static readonly string[] KeyboardClassTerms = ["keyboard", "keypad"];

    private static readonly HashSet<string> KeyboardAnchorNames = new(StringComparer.Ordinal)
    {
        "ESC", "ESCAPE", "TAB", "CAPS", "CAPSLOCK", "BACK", "BACKSPACE",
        "ENTER", "RETURN", "SPACE", "SPACEBAR", "LEFTSHIFT", "LSHIFT",
        "RIGHTSHIFT", "RSHIFT", "LEFTCTRL", "LCTRL", "LEFTCONTROL",
        "RIGHTCTRL", "RCTRL", "RIGHTCONTROL"
    };

    internal static bool IsKeyboard(Device device) =>
        IsKeyboard(device.Type, device.Name, device.Description, device.Leds?.Select(led => led.Name));

    internal static bool IsKeyboard(
        DeviceType type,
        string? name,
        string? description,
        IEnumerable<string?>? ledNames)
    {
        if (type == DeviceType.Keyboard) return true;

        // Do not second-guess an explicit OpenRGB classification. In particular,
        // this prevents another product from a keyboard vendor being listed here.
        if (type != DeviceType.Unknown) return false;

        return HasKeyboardClassLabel(name)
            || HasKeyboardClassLabel(description)
            || HasKeyboardLayout(ledNames);
    }

    private static bool HasKeyboardClassLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (KeyboardClassTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase))) return true;

        return SplitWords(value).Any(word => string.Equals(word, "kbd", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasKeyboardLayout(IEnumerable<string?>? ledNames)
    {
        if (ledNames is null) return false;

        var normalizedNames = ledNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => NormalizeLedName(name!))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var letterKeyCount = normalizedNames.Count(name => name.Length == 1 && name[0] is >= 'A' and <= 'Z');
        var numberKeyCount = normalizedNames.Count(name => name.Length == 1 && char.IsAsciiDigit(name[0]));
        var anchorKeyCount = normalizedNames.Count(KeyboardAnchorNames.Contains);

        // Several navigation/modifier keys plus a meaningful alphanumeric run
        // describe a keyboard layout without assuming any manufacturer or model.
        return letterKeyCount >= 10
            && anchorKeyCount >= 3
            && letterKeyCount + numberKeyCount + anchorKeyCount >= 16;
    }

    private static string NormalizeLedName(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("KEY_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("KEY:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        return new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static IEnumerable<string> SplitWords(string value)
    {
        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            if (index < value.Length && char.IsLetterOrDigit(value[index]))
            {
                if (start < 0) start = index;
                continue;
            }

            if (start < 0) continue;
            yield return value[start..index];
            start = -1;
        }
    }
}
