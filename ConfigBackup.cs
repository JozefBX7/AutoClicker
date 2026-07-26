using System.IO;
using System.Text.Json;

namespace AutoClicker;

internal sealed class ConfigBackupDocument
{
    // Supports future backup migrations.
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DefaultsJson { get; set; } = string.Empty;
    public string RgbJson { get; set; } = string.Empty;
    public string UiPreferencesJson { get; set; } = string.Empty;
    public string AppearanceJson { get; set; } = string.Empty;
    public string SequenceLibraryJson { get; set; } = string.Empty;
}

internal static class ConfigBackupStore
{
    internal const int CurrentSchemaVersion = 1;

    internal static ConfigBackupDocument Read(string path)
    {
        var document = JsonSerializer.Deserialize<ConfigBackupDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("The backup file is empty.");
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion) throw new InvalidDataException($"This backup uses unsupported schema version {document.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.DefaultsJson)) throw new InvalidDataException("The backup does not contain AutoClicker settings.");
        return document;
    }

    internal static void Write(string path, ConfigBackupDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        // Replace only after the complete backup has been written.
        File.Move(temporary, path, overwrite: true);
    }
}
