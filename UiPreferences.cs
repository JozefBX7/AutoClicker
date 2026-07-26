using System.Text.Json;
using System.IO;

namespace AutoClicker;

internal sealed class UiPreferences
{
    public bool Pinned { get; set; }
    public bool CompactMode { get; set; }
    public bool RgbLightingTipSeen { get; set; }
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
