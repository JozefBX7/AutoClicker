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
        BackupScope.SimpleMode => $"{AppIdentity.Name}-simple-settings",
        BackupScope.AdvancedMode => $"{AppIdentity.Name}-advanced-settings",
        BackupScope.CustomSequences => $"{AppIdentity.Name}-custom-sequences",
        _ => $"{AppIdentity.Name}-backup"
    };

    internal static string FileExtension(BackupScope scope) => scope switch
    {
        BackupScope.SimpleMode => ConfigurationFileExtensions.SimpleBackup,
        BackupScope.AdvancedMode => ConfigurationFileExtensions.AdvancedBackup,
        BackupScope.CustomSequences => ConfigurationFileExtensions.SequenceBackup,
        _ => ConfigurationFileExtensions.CompleteBackup
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
            return $"Complete {AppIdentity.Name} backups ({complete})|{complete}|Legacy {AppIdentity.Name} backups (*.json)|*.json";

        return $"Supported backups ({specific};{complete})|{specific};{complete}|{DisplayName(scope)} only ({specific})|{specific}|Complete {AppIdentity.Name} backups ({complete})|{complete}|Legacy {AppIdentity.Name} backups (*.json)|*.json";
    }

    private static string Wildcard(BackupScope scope) => "*" + FileExtension(scope);
}
