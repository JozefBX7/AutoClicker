// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoClicker;

internal enum AppTheme { Dark, Light }

internal static class ThemeManager
{
    private static readonly string AppearanceSettingsPath = AppPaths.ConfigFile(ConfigurationFileNames.AppearanceSettings);
    internal static AppTheme Current { get; private set; } = AppTheme.Dark;

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        [ThemeResourceKeys.AppBackgroundBrush] = "#F4F7FB", [ThemeResourceKeys.AppBorderBrush] = "#CBD5E1", [ThemeResourceKeys.PanelBrush] = "#FFFFFF",
        [ThemeResourceKeys.InputBrush] = "#F8FAFC", [ThemeResourceKeys.ControlBrush] = "#EEF4FF", [ThemeResourceKeys.ControlHoverBrush] = "#E0ECFF",
        [ThemeResourceKeys.CardBorderBrush] = "#D7E2F1", [ThemeResourceKeys.ControlBorderBrush] = "#C4D2E5", [ThemeResourceKeys.InputBorderBrush] = "#C4D2E5", [ThemeResourceKeys.LiveBorderBrush] = "#C7DBF5",
        [ThemeResourceKeys.TextPrimaryBrush] = "#172033", [ThemeResourceKeys.TextStrongBrush] = "#0F172A", [ThemeResourceKeys.TextSecondaryBrush] = "#334155", [ThemeResourceKeys.TextMutedBrush] = "#64748B", [ThemeResourceKeys.TextHintBrush] = "#64748B", [ThemeResourceKeys.LiveAccentTextBrush] = "#FFFFFF", [ThemeResourceKeys.TextAccentBrush] = "#1E3A8A", [ThemeResourceKeys.HotkeyTextBrush] = "#1E3A8A",
        [ThemeResourceKeys.AccentBrush] = "#60A5FA", [ThemeResourceKeys.PrimaryButtonTextBrush] = "#0F172A", [ThemeResourceKeys.AccentHoverBrush] = "#7DD3FC", [ThemeResourceKeys.AccentFocusBrush] = "#3B82F6", [ThemeResourceKeys.AccentPressedBrush] = "#3B82F6", [ThemeResourceKeys.AccentPaleBrush] = "#BFDBFE",
        [ThemeResourceKeys.DisabledBrush] = "#D2DCE9", [ThemeResourceKeys.DisabledBorderBrush] = "#B9C7D9", [ThemeResourceKeys.DisabledTextBrush] = "#64748B", [ThemeResourceKeys.DangerBrush] = "#DC2626", [ThemeResourceKeys.DangerHoverBrush] = "#EF4444", [ThemeResourceKeys.DangerPressedBrush] = "#B91C1C", [ThemeResourceKeys.DangerPaleBrush] = "#FCA5A5",
        [ThemeResourceKeys.CloseHoverBrush] = "#FFF1F2", [ThemeResourceKeys.CloseHoverBorderBrush] = "#FB7185", [ThemeResourceKeys.CloseHoverTextBrush] = "#9F1239", [ThemeResourceKeys.LiveFlashBrush] = "#DBEAFE", [ThemeResourceKeys.LiveFlashBorderBrush] = "#3B82F6",
        [ThemeResourceKeys.SuccessBrush] = "#15803D", [ThemeResourceKeys.WarningBrush] = "#A16207", [ThemeResourceKeys.ErrorBrush] = "#DC2626"
    };

    internal static void Load()
    {
        try
        {
            if (File.Exists(AppearanceSettingsPath) && JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(AppearanceSettingsPath)) is { } settings)
            {
                Apply(settings.Theme, persist: false);
                return;
            }
        }
        catch { }

        // No saved choice: follow Windows without creating a preference file.
        Apply(SystemTheme(), persist: false);
    }

    internal static void Apply(AppTheme theme, bool persist = true)
    {
        Current = theme;
        if (theme == AppTheme.Light)
            foreach (var (key, color) in LightPalette) Application.Current.Resources[key] = MakeBrush(color);
        else
            RestoreDarkPalette();

        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppearanceSettingsPath)!);
            File.WriteAllText(AppearanceSettingsPath, JsonSerializer.Serialize(new AppearanceSettings { Theme = theme }));
        }
        catch { }
    }

    private static void RestoreDarkPalette()
    {
        var dark = new Dictionary<string, string>
        {
            [ThemeResourceKeys.AppBackgroundBrush] = "#0B1220", [ThemeResourceKeys.AppBorderBrush] = "#26364E", [ThemeResourceKeys.PanelBrush] = "#101A2D", [ThemeResourceKeys.InputBrush] = "#111C30", [ThemeResourceKeys.ControlBrush] = "#17243A", [ThemeResourceKeys.ControlHoverBrush] = "#2A3E5D", [ThemeResourceKeys.CardBorderBrush] = "#20314A", [ThemeResourceKeys.ControlBorderBrush] = "#31445F", [ThemeResourceKeys.InputBorderBrush] = "#26364E", [ThemeResourceKeys.LiveBorderBrush] = "#1E3A5F", [ThemeResourceKeys.TextPrimaryBrush] = "#E5EDF9", [ThemeResourceKeys.TextStrongBrush] = "#F8FAFC", [ThemeResourceKeys.TextSecondaryBrush] = "#C7D2E4", [ThemeResourceKeys.TextMutedBrush] = "#94A3B8", [ThemeResourceKeys.TextHintBrush] = "#71829B", [ThemeResourceKeys.LiveAccentTextBrush] = "#94A3B8", [ThemeResourceKeys.TextAccentBrush] = "#C7D2FE", [ThemeResourceKeys.HotkeyTextBrush] = "#E0E7FF", [ThemeResourceKeys.AccentBrush] = "#2563EB", [ThemeResourceKeys.PrimaryButtonTextBrush] = "#FFFFFF", [ThemeResourceKeys.AccentHoverBrush] = "#3B82F6", [ThemeResourceKeys.AccentFocusBrush] = "#60A5FA", [ThemeResourceKeys.AccentPressedBrush] = "#1D4ED8", [ThemeResourceKeys.AccentPaleBrush] = "#93C5FD", [ThemeResourceKeys.DisabledBrush] = "#182438", [ThemeResourceKeys.DisabledBorderBrush] = "#2B3D57", [ThemeResourceKeys.DisabledTextBrush] = "#71829B", [ThemeResourceKeys.DangerBrush] = "#DC2626", [ThemeResourceKeys.DangerHoverBrush] = "#EF4444", [ThemeResourceKeys.DangerPressedBrush] = "#B91C1C", [ThemeResourceKeys.DangerPaleBrush] = "#FCA5A5", [ThemeResourceKeys.CloseHoverBrush] = "#4C1D2A", [ThemeResourceKeys.CloseHoverBorderBrush] = "#FB7185", [ThemeResourceKeys.CloseHoverTextBrush] = "#FFE4E6", [ThemeResourceKeys.LiveFlashBrush] = "#1D4ED8", [ThemeResourceKeys.LiveFlashBorderBrush] = "#BFDBFE", [ThemeResourceKeys.SuccessBrush] = "#90EE90", [ThemeResourceKeys.WarningBrush] = "#DAA520", [ThemeResourceKeys.ErrorBrush] = "#FA8072"
        };
        foreach (var (key, color) in dark) Application.Current.Resources[key] = MakeBrush(color);
    }

    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    // Status messages receive concrete brushes, so retain the semantic key to reapply it after a palette swap.
    internal static string? StatusBrushKey(Brush brush)
    {
        foreach (var key in new[] { ThemeResourceKeys.SuccessBrush, ThemeResourceKeys.WarningBrush, ThemeResourceKeys.ErrorBrush, ThemeResourceKeys.TextMutedBrush })
            if (ReferenceEquals(Brush(key), brush)) return key;
        return null;
    }

    internal static AppTheme ThemeFromAppsUseLightTheme(object? value) => value is int enabled && enabled != 0 ? AppTheme.Light : AppTheme.Dark;

    private static AppTheme SystemTheme()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return ThemeFromAppsUseLightTheme(personalize?.GetValue("AppsUseLightTheme"));
        }
        catch
        {
            // Default to dark if Windows does not expose a preference.
            return AppTheme.Dark;
        }
    }

    internal static string ExportConfiguration() => JsonSerializer.Serialize(new AppearanceSettings { Theme = Current });

    internal static bool TryImportConfiguration(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(json);
            if (settings is null || !Enum.IsDefined(settings.Theme)) return false;
            Apply(settings.Theme);
            return true;
        }
        catch { return false; }
    }

    private static SolidColorBrush MakeBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private sealed class AppearanceSettings { public AppTheme Theme { get; set; } = AppTheme.Dark; }
}
