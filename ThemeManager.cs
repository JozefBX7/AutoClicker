using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoClicker;

internal enum AppTheme { Dark, Light }

internal static class ThemeManager
{
    private static readonly string SettingsPath = AppPaths.ConfigFile("appearance.json");
    internal static AppTheme Current { get; private set; } = AppTheme.Dark;

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["AppBackgroundBrush"] = "#F4F7FB", ["AppBorderBrush"] = "#CBD5E1", ["PanelBrush"] = "#FFFFFF",
        ["InputBrush"] = "#F8FAFC", ["ControlBrush"] = "#EEF4FF", ["ControlHoverBrush"] = "#E0ECFF",
        ["CardBorderBrush"] = "#D7E2F1", ["ControlBorderBrush"] = "#C4D2E5", ["InputBorderBrush"] = "#C4D2E5", ["LiveBorderBrush"] = "#C7DBF5",
        ["TextPrimaryBrush"] = "#172033", ["TextStrongBrush"] = "#0F172A", ["TextSecondaryBrush"] = "#334155", ["TextMutedBrush"] = "#64748B", ["TextHintBrush"] = "#64748B", ["LiveAccentTextBrush"] = "#FFFFFF", ["TextAccentBrush"] = "#1E3A8A", ["HotkeyTextBrush"] = "#1E3A8A",
        ["AccentBrush"] = "#60A5FA", ["PrimaryButtonTextBrush"] = "#0F172A", ["AccentHoverBrush"] = "#7DD3FC", ["AccentFocusBrush"] = "#3B82F6", ["AccentPressedBrush"] = "#3B82F6", ["AccentPaleBrush"] = "#BFDBFE",
        ["DisabledBrush"] = "#D2DCE9", ["DisabledBorderBrush"] = "#B9C7D9", ["DisabledTextBrush"] = "#64748B", ["DangerBrush"] = "#DC2626", ["DangerHoverBrush"] = "#EF4444", ["DangerPressedBrush"] = "#B91C1C", ["DangerPaleBrush"] = "#FCA5A5",
        ["CloseHoverBrush"] = "#FFF1F2", ["CloseHoverBorderBrush"] = "#FB7185", ["CloseHoverTextBrush"] = "#9F1239", ["LiveFlashBrush"] = "#DBEAFE", ["LiveFlashBorderBrush"] = "#3B82F6",
        ["SuccessBrush"] = "#15803D", ["WarningBrush"] = "#A16207", ["ErrorBrush"] = "#DC2626"
    };

    internal static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath) && JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(SettingsPath)) is { } settings)
            {
                Apply(settings.Theme, persist: false);
                return;
            }
        }
        catch { }

        // Do not create an appearance file here: until the user chooses a theme,
        // a fresh installation should continue to follow their Windows preference.
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
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppearanceSettings { Theme = theme }));
        }
        catch { }
    }

    private static void RestoreDarkPalette()
    {
        var dark = new Dictionary<string, string>
        {
            ["AppBackgroundBrush"] = "#0B1220", ["AppBorderBrush"] = "#26364E", ["PanelBrush"] = "#101A2D", ["InputBrush"] = "#111C30", ["ControlBrush"] = "#17243A", ["ControlHoverBrush"] = "#2A3E5D", ["CardBorderBrush"] = "#20314A", ["ControlBorderBrush"] = "#31445F", ["InputBorderBrush"] = "#26364E", ["LiveBorderBrush"] = "#1E3A5F", ["TextPrimaryBrush"] = "#E5EDF9", ["TextStrongBrush"] = "#F8FAFC", ["TextSecondaryBrush"] = "#C7D2E4", ["TextMutedBrush"] = "#94A3B8", ["TextHintBrush"] = "#71829B", ["LiveAccentTextBrush"] = "#94A3B8", ["TextAccentBrush"] = "#C7D2FE", ["HotkeyTextBrush"] = "#E0E7FF", ["AccentBrush"] = "#2563EB", ["PrimaryButtonTextBrush"] = "#FFFFFF", ["AccentHoverBrush"] = "#3B82F6", ["AccentFocusBrush"] = "#60A5FA", ["AccentPressedBrush"] = "#1D4ED8", ["AccentPaleBrush"] = "#93C5FD", ["DisabledBrush"] = "#182438", ["DisabledBorderBrush"] = "#2B3D57", ["DisabledTextBrush"] = "#71829B", ["DangerBrush"] = "#DC2626", ["DangerHoverBrush"] = "#EF4444", ["DangerPressedBrush"] = "#B91C1C", ["DangerPaleBrush"] = "#FCA5A5", ["CloseHoverBrush"] = "#4C1D2A", ["CloseHoverBorderBrush"] = "#FB7185", ["CloseHoverTextBrush"] = "#FFE4E6", ["LiveFlashBrush"] = "#1D4ED8", ["LiveFlashBorderBrush"] = "#BFDBFE", ["SuccessBrush"] = "#90EE90", ["WarningBrush"] = "#DAA520", ["ErrorBrush"] = "#FA8072"
        };
        foreach (var (key, color) in dark) Application.Current.Resources[key] = MakeBrush(color);
    }

    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

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
            // Retain the established dark theme if the preference is unavailable.
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
        // Frozen brushes are immutable and safe to reuse throughout the visual tree.
        brush.Freeze();
        return brush;
    }

    private sealed class AppearanceSettings { public AppTheme Theme { get; set; } = AppTheme.Dark; }
}
