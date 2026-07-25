using System.Text.Json;
using System.IO;

namespace AutoClicker;

public sealed class SequencePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled sequence";
    public List<SequenceStep> Steps { get; set; } = [];
    public SequencePreset Clone() => new() { Id = Id, Name = Name, Steps = Steps.Select(step => step.Clone()).ToList() };
    public override string ToString() => Name;
}

internal sealed class SequenceLibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<SequencePreset> Presets { get; set; } = [];
}

internal static class SequenceLibraryStore
{
    internal static List<SequencePreset> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SequenceLibraryDocument>(File.ReadAllText(path))?.Presets
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Id) && !string.IsNullOrWhiteSpace(preset.Name) && preset.Steps.Count >= 2)
                .Select(preset => preset.Clone()).ToList() ?? [];
        }
        catch { return []; }
    }

    internal static void Save(string path, IEnumerable<SequencePreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new SequenceLibraryDocument { Presets = presets.Select(preset => preset.Clone()).ToList() };
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document));
        File.Move(temporary, path, overwrite: true);
    }
}
