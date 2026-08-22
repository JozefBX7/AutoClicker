// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public enum BackupScope
{
    Everything,
    SimpleMode,
    AdvancedMode,
    CustomSequences
}

internal static class BackupScopeInfo
{
    internal static string DisplayName(BackupScope scope) => scope switch
    {
        BackupScope.SimpleMode => "Simple mode settings",
        BackupScope.AdvancedMode => "Advanced mode settings and profiles",
        BackupScope.CustomSequences => "Custom sequences",
        _ => "Everything"
    };

    internal static string FileStem(BackupScope scope) => scope switch
    {
        BackupScope.SimpleMode => "AutoClicker-simple-settings",
        BackupScope.AdvancedMode => "AutoClicker-advanced-settings",
        BackupScope.CustomSequences => "AutoClicker-custom-sequences",
        _ => "AutoClicker-backup"
    };

    internal static string FileExtension(BackupScope scope) => scope switch
    {
        BackupScope.SimpleMode => ".autoclicker-simple.json",
        BackupScope.AdvancedMode => ".autoclicker-advanced.json",
        BackupScope.CustomSequences => ".autoclicker-sequences.json",
        _ => ".autoclicker-backup.json"
    };

    internal static string DefaultFileName(BackupScope scope) => FileStem(scope) + FileExtension(scope);

    internal static string ExportFilter(BackupScope scope) =>
        $"{DisplayName(scope)} ({Wildcard(scope)})|{Wildcard(scope)}";

    // Focused restores can safely take their section from a complete backup as well.
    // The last entry remains available for older .json backups made before typed extensions.
    internal static string ImportFilter(BackupScope scope)
    {
        var specific = Wildcard(scope);
        var complete = Wildcard(BackupScope.Everything);
        if (scope == BackupScope.Everything)
            return $"Complete AutoClicker backups ({complete})|{complete}|Legacy AutoClicker backups (*.json)|*.json";

        return $"Supported backups ({specific};{complete})|{specific};{complete}|{DisplayName(scope)} only ({specific})|{specific}|Complete AutoClicker backups ({complete})|{complete}|Legacy AutoClicker backups (*.json)|*.json";
    }

    private static string Wildcard(BackupScope scope) => "*" + FileExtension(scope);
}
