// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public static class ConfigurationFileNames
{
    public const string SimpleDefaults = "defaults.json";
    public const string AdvancedSharedDefaults = "global-defaults.json";
    public const string RgbSettings = "rgb-settings.json";
    public const string ApplicationPreferences = "application-preferences.json";
    public const string LegacyUiPreferences = "ui-preferences.json";
    public const string SequenceLibrary = "sequence-library.json";
    public const string AutomationProfiles = "automation-profiles.json";
    public const string AppearanceSettings = "appearance.json";
    public const string CrashHistory = "crash-history.json";
    public const string Log = "AutoClicker.log";
    public const string EndToEndRuntimeLog = "e2e-runtime-events.log";
    public const string TemporarySuffix = ".tmp";
}

public static class ApplicationPreferenceJsonNames
{
    public const string LegacyCrashRecoveryEnabled = "CrashRecoveryEnabled";
}

public static class ConfigurationFileExtensions
{
    public const string Profile = ".autoclicker-profile.json";
    public const string CompleteBackup = ".autoclicker-backup.json";
    public const string SimpleBackup = ".autoclicker-simple.json";
    public const string AdvancedBackup = ".autoclicker-advanced.json";
    public const string SequenceBackup = ".autoclicker-sequences.json";
}
