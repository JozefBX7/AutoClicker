// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Text.Json;
using System.IO;

namespace AutoClicker;

public sealed class SequencePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat);
    public string Name { get; set; } = "Untitled sequence";
    public List<SequenceStep> Steps { get; set; } = [];
    public bool UseGlobalInputPulse { get; set; } = true;
    public SequencePreset Clone() => new() { Id = Id, Name = Name, Steps = Steps.Select(step => step.Clone()).ToList(), UseGlobalInputPulse = UseGlobalInputPulse };
    public override string ToString() => Name;
}

internal sealed class SequenceLibraryDocument
{
    public int SchemaVersion { get; set; } = SequenceLibraryStore.CurrentSchemaVersion;
    public List<SequencePreset> Presets { get; set; } = [];
}

internal static class SequenceLibraryStore
{
    internal const int CurrentSchemaVersion = 2;

    internal static List<SequencePreset> Load(string path)
    {
        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch { return []; }
    }

    internal static List<SequencePreset> Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<SequenceLibraryDocument>(json) ?? throw new InvalidDataException("The sequence library is empty.");
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion)
            throw new InvalidDataException($"This sequence library uses unsupported schema version {document.SchemaVersion}.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var presets = new List<SequencePreset>();
        foreach (var preset in document.Presets ?? [])
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name) || !seenIds.Add(preset.Id) || preset.Steps is null) continue;

            var steps = new List<SequenceStep>();
            var isValid = true;
            foreach (var step in preset.Steps)
            {
                if (step is null || !IsSupportedStep(step)) { isValid = false; break; }
                steps.Add(NormalizeStep(step));
            }

            if (isValid && steps.Count >= 2 && SequenceHoldRules.ValidationError(steps) is null)
                presets.Add(new SequencePreset { Id = preset.Id, Name = preset.Name.Trim(), Steps = steps, UseGlobalInputPulse = preset.UseGlobalInputPulse });
        }

        return presets;
    }

    internal static void Save(string path, IEnumerable<SequencePreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new SequenceLibraryDocument { Presets = Deserialize(JsonSerializer.Serialize(new SequenceLibraryDocument { Presets = presets.Select(preset => preset.Clone()).ToList() })) };
        var temporary = path + ConfigurationFileNames.TemporarySuffix;
        File.WriteAllText(temporary, JsonSerializer.Serialize(document));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsSupportedStep(SequenceStep step) => Enum.IsDefined(step.Mode) && step.Input switch
    {
        AutomationInputIds.Left or AutomationInputIds.Right or AutomationInputIds.Middle or AutomationInputIds.Space or AutomationInputIds.Enter => true,
        AutomationInputIds.Custom => step.CustomKey is > 0 and <= 0xFF,
        AutomationInputIds.Delay => true,
        _ => false
    };

    private static SequenceStep NormalizeStep(SequenceStep step) => step.Input == AutomationInputIds.Delay
        ? new SequenceStep { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = Math.Clamp(step.DelayAfterMilliseconds, 1, 600000), Mode = SequenceStepMode.Press }
        : new SequenceStep { Input = step.Input, CustomKey = step.CustomKey, DelayAfterMilliseconds = Math.Clamp(step.DelayAfterMilliseconds, 0, 600000), Mode = step.Mode };
}
