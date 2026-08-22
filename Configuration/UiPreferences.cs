// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Text.Json;
using System.IO;

namespace AutoClicker;

internal sealed class UiPreferences
{
    public bool Pinned { get; set; }
    public bool CompactMode { get; set; }
    public bool QuickStartSeen { get; set; }
    public string WorkerPriority { get; set; } = WorkerPriorityOption.Normal.ToString();
    public bool CadenceDiagnosticsEnabled { get; set; }
    public bool AdvancedMode { get; set; }
    public bool KeyboardHotkeyModifiersEnabled { get; set; }
}

public enum WorkerPriorityOption { Normal, AboveNormal }

internal static class WorkerPriorityRules
{
    internal static WorkerPriorityOption Normalize(string? value) => Enum.TryParse<WorkerPriorityOption>(value, ignoreCase: true, out var priority)
        ? priority
        : WorkerPriorityOption.Normal;
}

internal static class UiPreferencesStore
{
    internal static UiPreferences Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path)) ?? new UiPreferences()
                : new UiPreferences();
        }
        catch { return new UiPreferences(); }
    }

    internal static void Save(string path, UiPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(preferences));
    }
}
