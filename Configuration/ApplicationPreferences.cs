// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;

namespace AutoClicker;

public sealed class ApplicationPreferences
{
    public bool Pinned { get; set; }
    public bool RememberPinned { get; set; } = true;
    public bool ApplyPinnedOnLaunch { get; set; } = true;
    public bool CompactMode { get; set; }
    public bool QuickStartSeen { get; set; }
    public string WorkerPriority { get; set; } = WorkerPriorityOption.Normal.ToString();
    public bool CadenceDiagnosticsEnabled { get; set; }
    public bool AdvancedMode { get; set; }
    public bool KeyboardHotkeyModifiersEnabled { get; set; }
    public bool CrashRecoveryEnabled { get; set; } = true;
    public PersistedWindowPosition? MainWindowPosition { get; set; }

    internal ApplicationPreferences Clone() => new()
    {
        Pinned = Pinned,
        RememberPinned = RememberPinned,
        ApplyPinnedOnLaunch = ApplyPinnedOnLaunch,
        CompactMode = CompactMode,
        QuickStartSeen = QuickStartSeen,
        WorkerPriority = WorkerPriority,
        CadenceDiagnosticsEnabled = CadenceDiagnosticsEnabled,
        AdvancedMode = AdvancedMode,
        KeyboardHotkeyModifiersEnabled = KeyboardHotkeyModifiersEnabled,
        CrashRecoveryEnabled = CrashRecoveryEnabled,
        MainWindowPosition = MainWindowPosition?.Clone()
    };
}

public sealed class PersistedWindowPosition
{
    public int Left { get; set; }
    public int Top { get; set; }

    internal PersistedWindowPosition Clone() => new() { Left = Left, Top = Top };
}

internal static class PinnedWindowPreferenceRules
{
    internal static bool ApplyOnLaunch(ApplicationPreferences preferences) =>
        preferences.RememberPinned && preferences.ApplyPinnedOnLaunch && preferences.Pinned;

    internal static bool DeferUntilInteraction(ApplicationPreferences preferences) =>
        preferences.RememberPinned && !preferences.ApplyPinnedOnLaunch && preferences.Pinned;

    internal static bool PersistedPinnedState(bool rememberPinned, bool currentPinnedState) =>
        rememberPinned && currentPinnedState;
}

public enum WorkerPriorityOption { Normal, AboveNormal }

internal static class WorkerPriorityRules
{
    internal static WorkerPriorityOption Normalize(string? value) => Enum.TryParse<WorkerPriorityOption>(value, ignoreCase: true, out var priority)
        ? priority
        : WorkerPriorityOption.Normal;
}

internal static class ApplicationPreferencesStore
{
    internal static ApplicationPreferences Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(path)) ?? new ApplicationPreferences()
                : new ApplicationPreferences();
        }
        catch { return new ApplicationPreferences(); }
    }

    internal static ApplicationPreferences LoadWithLegacyFallback(
        string path,
        string legacyUiPreferencesPath,
        string legacyRgbSettingsPath)
    {
        if (File.Exists(path)) return Load(path);

        var preferences = Load(legacyUiPreferencesPath);
        if (TryReadLegacyCrashRecoveryEnabled(legacyRgbSettingsPath) is { } enabled)
            preferences.CrashRecoveryEnabled = enabled;
        return preferences;
    }

    internal static bool? TryReadLegacyCrashRecoveryEnabled(string rgbSettingsPath)
    {
        try
        {
            if (!File.Exists(rgbSettingsPath)) return null;
            return TryReadLegacyCrashRecoveryEnabledFromJson(File.ReadAllText(rgbSettingsPath));
        }
        catch { return null; }
    }

    internal static bool? TryReadLegacyCrashRecoveryEnabledFromJson(string? rgbSettingsJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rgbSettingsJson)) return null;
            using var document = JsonDocument.Parse(rgbSettingsJson);
            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, ApplicationPreferenceJsonNames.LegacyCrashRecoveryEnabled, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return property.Value.GetBoolean();
        }
        catch { }
        return null;
    }

    internal static void Save(string path, ApplicationPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(preferences));
    }
}

internal static class ApplicationPreferencesRepository
{
    private static string PreferencesPath => AppPaths.ConfigFile(ConfigurationFileNames.ApplicationPreferences);
    private static string LegacyUiPreferencesPath => AppPaths.ConfigFile(ConfigurationFileNames.LegacyUiPreferences);
    private static string LegacyRgbSettingsPath => AppPaths.ConfigFile(ConfigurationFileNames.RgbSettings);

    internal static ApplicationPreferences Load()
    {
        var currentExists = File.Exists(PreferencesPath);
        var preferences = ApplicationPreferencesStore.LoadWithLegacyFallback(PreferencesPath, LegacyUiPreferencesPath, LegacyRgbSettingsPath);
        if (!currentExists && (File.Exists(LegacyUiPreferencesPath) || File.Exists(LegacyRgbSettingsPath)))
        {
            try { Save(preferences); } catch { }
        }
        return preferences;
    }

    internal static void Save(ApplicationPreferences preferences) =>
        ApplicationPreferencesStore.Save(PreferencesPath, preferences);
}
