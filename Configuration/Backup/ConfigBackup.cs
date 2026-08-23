// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoClicker;

internal sealed class ConfigBackupDocument
{
    // Supports future backup migrations.
    public int SchemaVersion { get; set; } = 4;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public BackupScope Scope { get; set; } = BackupScope.Everything;
    // The serialized name is retained for schema-2 compatibility. New backups keep the modes separate.
    [JsonPropertyName("DefaultsJson")]
    public string LegacySharedDefaultsJson { get; set; } = string.Empty;
    public string SimpleDefaultsJson { get; set; } = string.Empty;
    public string AdvancedDefaultsJson { get; set; } = string.Empty;
    public string RgbJson { get; set; } = string.Empty;
    public string ApplicationPreferencesJson { get; set; } = string.Empty;
    // The serialized name is retained for schema-3 backup compatibility.
    [JsonPropertyName("UiPreferencesJson")]
    public string LegacyApplicationPreferencesJson { get; set; } = string.Empty;
    public string AppearanceJson { get; set; } = string.Empty;
    public string SequenceLibraryJson { get; set; } = string.Empty;
    public string AutomationProfilesJson { get; set; } = string.Empty;
}

internal static class ConfigBackupStore
{
    internal const int CurrentSchemaVersion = 4;

    internal static ConfigBackupDocument Read(string path)
    {
        var document = JsonSerializer.Deserialize<ConfigBackupDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("The backup file is empty.");
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion) throw new InvalidDataException($"This backup uses unsupported schema version {document.SchemaVersion}.");
        if (document.SchemaVersion < 3) document.Scope = BackupScope.Everything;
        else if (!Enum.IsDefined(document.Scope)) throw new InvalidDataException("The backup uses an unsupported export scope.");
        if (string.IsNullOrWhiteSpace(document.LegacySharedDefaultsJson)
            && string.IsNullOrWhiteSpace(document.SimpleDefaultsJson)
            && string.IsNullOrWhiteSpace(document.AdvancedDefaultsJson)
            && string.IsNullOrWhiteSpace(document.SequenceLibraryJson)
            && string.IsNullOrWhiteSpace(document.AutomationProfilesJson)
            && string.IsNullOrWhiteSpace(document.RgbJson)
            && string.IsNullOrWhiteSpace(document.ApplicationPreferencesJson)
            && string.IsNullOrWhiteSpace(document.LegacyApplicationPreferencesJson)
            && string.IsNullOrWhiteSpace(document.AppearanceJson))
            throw new InvalidDataException("The backup does not contain any AutoClicker settings.");
        return document;
    }

    internal static void Write(string path, ConfigBackupDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ConfigurationFileNames.TemporarySuffix;
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        // Replace only after the complete backup has been written.
        File.Move(temporary, path, overwrite: true);
    }
}
