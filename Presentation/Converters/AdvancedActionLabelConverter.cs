// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace AutoClicker;

// Keeps mouse assignments legible until space is scarce, then gives keys priority over prose.
public sealed class AdvancedActionLabelConverter : IMultiValueConverter
{
    // Tile width includes the border, padding, and the two fixed action buttons.
    private const double NonLabelWidth = 48;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not AutomationAction action) return string.Empty;
        var tileWidth = values[1] is double actualWidth ? actualWidth : double.MaxValue;
        var hasInlineActionControls = values.Length < 3 || values[2] is not bool visible || visible;
        var labelWidth = Math.Max(0, tileWidth - (hasInlineActionControls ? NonLabelWidth : 0));
        if (action.Settings.Input == AutomationInputIds.Unset) return AutomationInputLabels.SetAction;
        var label = Describe(action.Settings, out var isMouseAction, out var compactSymbol);

        if (labelWidth >= EstimateWidth(label)) return label;
        return isMouseAction && labelWidth >= 14 ? compactSymbol : string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static string Describe(AppDefaults settings, out bool isMouseAction, out string compactSymbol)
    {
        isMouseAction = false;
        compactSymbol = string.Empty;
        string input;
        switch (settings.Input)
        {
            case AutomationInputIds.Space: input = AutomationInputIds.Space; break;
            case AutomationInputIds.Enter: input = AutomationInputIds.Enter; break;
            case AutomationInputIds.Custom when settings.CustomKey != 0: input = KeyInterop.KeyFromVirtualKey(settings.CustomKey).ToString(); break;
            case AutomationInputIds.Sequence: return InputRules.IsWhileHeldAction(settings.ClickType) ? "Sequence while held" : AutomationInputIds.Sequence;
            default:
                isMouseAction = true;
                input = (settings.Input ?? settings.MouseButton) switch
                {
                    AutomationInputIds.Right => SetMouse(AutomationInputLabels.RightClick, "R", out compactSymbol),
                    AutomationInputIds.Middle => SetMouse(AutomationInputLabels.MiddleClick, "M", out compactSymbol),
                    AutomationInputIds.Mouse4 => SetMouse(AutomationInputLabels.Mouse4Click, "4", out compactSymbol),
                    AutomationInputIds.Mouse5 => SetMouse(AutomationInputLabels.Mouse5Click, "5", out compactSymbol),
                    AutomationInputIds.ScrollUp => SetMouse(AutomationInputLabels.ScrollUp, "↑", out compactSymbol),
                    AutomationInputIds.ScrollDown => SetMouse(AutomationInputLabels.ScrollDown, "↓", out compactSymbol),
                    AutomationInputIds.ScrollLeft => SetMouse(AutomationInputLabels.ScrollLeft, "←", out compactSymbol),
                    AutomationInputIds.ScrollRight => SetMouse(AutomationInputLabels.ScrollRight, "→", out compactSymbol),
                    _ => SetMouse(AutomationInputLabels.LeftClick, "L", out compactSymbol)
                };
                break;
        }
        var typedInput = isMouseAction ? char.ToLowerInvariant(input[0]) + input[1..] : input;
        return settings.ClickType switch
        {
            AutomationActionTypeIds.Double => $"Double {typedInput}",
            AutomationActionTypeIds.Hold => $"Hold {typedInput}",
            AutomationActionTypeIds.WhileHeld => $"{input} while held",
            _ => input
        };
    }

    private static string SetMouse(string label, string symbol, out string compactSymbol)
    {
        compactSymbol = symbol;
        return label;
    }

    private static double EstimateWidth(string label) => Math.Max(14, label.Length * 3 + 2);
}
