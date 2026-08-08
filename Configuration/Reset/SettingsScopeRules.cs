namespace AutoClicker;

// Keep reset and backup boundaries explicit: Simple settings never imply Advanced settings or profiles.
internal static class SettingsScopeRules
{
    internal static bool ResetsSimple(ResetScope scope) => scope is ResetScope.SimpleMode or ResetScope.Everything;
    internal static bool ResetsAdvancedProfiles(ResetScope scope) => scope is ResetScope.AdvancedMode or ResetScope.Everything;
    internal static bool ResetsAdvancedGlobals(ResetScope scope) => scope is ResetScope.SharedDefaults or ResetScope.Everything;

    internal static bool IncludesSimple(BackupScope scope) => scope is BackupScope.SimpleMode or BackupScope.Everything;
    internal static bool IncludesAdvanced(BackupScope scope) => scope is BackupScope.AdvancedMode or BackupScope.Everything;
    internal static bool IncludesSequences(BackupScope scope) => scope is BackupScope.CustomSequences or BackupScope.Everything;
    internal static bool IncludesAppSettings(BackupScope scope) => scope == BackupScope.Everything;
}
