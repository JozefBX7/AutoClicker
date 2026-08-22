// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AutoClicker.E2E;

internal sealed class ProfileE2EFixture : IDisposable
{
    internal const string ProfileId = "e2e-profile";
    internal const string ActionId = "e2e-action";
    internal const string SecondActionId = "e2e-action-2";
    internal const int GlobalMilliseconds = 100;
    internal const int ProfileMilliseconds = 200;
    internal const int HotkeyMilliseconds = 300;
    internal const int GlobalRepeatCount = 10;
    internal const int ProfileRepeatCount = 20;
    internal const int HotkeyRepeatCount = 30;

    internal ProfileE2EFixture(bool advancedMode = true, bool chordedKeyboardHotkeys = false)
    {
        ConfigDirectory = Path.Combine(Path.GetTempPath(), "AutoClicker.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ConfigDirectory);
        SeedConfiguration(advancedMode, chordedKeyboardHotkeys);
    }

    internal string ConfigDirectory { get; }

    internal AutoClickerE2ESession Launch(
        string? saveFile = null,
        string? openFile = null,
        bool registerKeyboardHotkeys = false) =>
        AutoClickerE2ESession.Launch(ConfigDirectory, saveFile, openFile, registerKeyboardHotkeys);

    internal string TestFile(string fileName) => Path.Combine(ConfigDirectory, fileName);

    internal void WriteSequenceLibrary(IEnumerable<SequencePreset> presets) =>
        SequenceLibraryStore.Save(Path.Combine(ConfigDirectory, "sequence-library.json"), presets);

    internal AppDefaults ReadGlobalDefaults() =>
        JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(Path.Combine(ConfigDirectory, "global-defaults.json")))
        ?? throw new InvalidDataException("Global defaults were not valid JSON.");

    internal AppDefaults ReadSimpleDefaults() =>
        JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(Path.Combine(ConfigDirectory, "defaults.json")))
        ?? throw new InvalidDataException("Simple defaults were not valid JSON.");

    internal AutomationProfileDocument ReadProfiles() =>
        AutomationProfileStore.Load(Path.Combine(ConfigDirectory, "automation-profiles.json"), new AppDefaults());

    internal IReadOnlyList<SequencePreset> ReadSequenceLibrary() =>
        SequenceLibraryStore.Load(Path.Combine(ConfigDirectory, "sequence-library.json"));

    internal UiPreferences ReadUiPreferences() =>
        UiPreferencesStore.Load(Path.Combine(ConfigDirectory, "ui-preferences.json"));

    internal RgbSettings ReadRgbSettings() =>
        JsonSerializer.Deserialize<RgbSettings>(File.ReadAllText(Path.Combine(ConfigDirectory, "rgb-settings.json")))
        ?? throw new InvalidDataException("RGB settings were not valid JSON.");

    internal string ReadAppearance() =>
        File.Exists(Path.Combine(ConfigDirectory, "appearance.json"))
            ? File.ReadAllText(Path.Combine(ConfigDirectory, "appearance.json"))
            : string.Empty;

    internal IReadOnlyList<string> ReadRuntimeEvents()
    {
        var path = Path.Combine(ConfigDirectory, "e2e-runtime-events.log");
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    public void Dispose()
    {
        try { if (Directory.Exists(ConfigDirectory)) Directory.Delete(ConfigDirectory, recursive: true); }
        catch { }
    }

    private void SeedConfiguration(bool advancedMode, bool chordedKeyboardHotkeys)
    {
        const uint controlAndShift = 0x2 | 0x4;
        var hotkeyModifiers = chordedKeyboardHotkeys ? controlAndShift : 0;
        var global = Defaults(GlobalMilliseconds, GlobalRepeatCount, 1, 2, "global.exe", hotkeyModifiers);
        File.WriteAllText(Path.Combine(ConfigDirectory, "global-defaults.json"), JsonSerializer.Serialize(global));
        File.WriteAllText(Path.Combine(ConfigDirectory, "defaults.json"), JsonSerializer.Serialize(Defaults(50, 5, 0, 0, "simple.exe", hotkeyModifiers)));
        UiPreferencesStore.Save(Path.Combine(ConfigDirectory, "ui-preferences.json"), new UiPreferences
        {
            AdvancedMode = advancedMode,
            QuickStartSeen = true,
            KeyboardHotkeyModifiersEnabled = chordedKeyboardHotkeys
        });
        File.WriteAllText(Path.Combine(ConfigDirectory, "rgb-settings.json"), JsonSerializer.Serialize(new RgbSettings
        {
            CrashRecoveryEnabled = false,
            AutoStart = false
        }));

        var action = new AutomationAction
        {
            Id = ActionId,
            Settings = Defaults(HotkeyMilliseconds, HotkeyRepeatCount, 5, 6, "hotkey.exe", hotkeyModifiers),
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.All
        };
        var secondSettings = Defaults(75, 7, 7, 8, string.Empty, hotkeyModifiers);
        secondSettings.TargetWindowEnabled = false;
        secondSettings.RepeatUntilStopped = true;
        secondSettings.Hotkey = 118;
        var secondAction = new AutomationAction
        {
            Id = SecondActionId,
            Settings = secondSettings,
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.All
        };
        var profile = new AutomationProfile
        {
            Id = ProfileId,
            Name = "E2E Profile",
            BehaviorDefaults = Defaults(ProfileMilliseconds, ProfileRepeatCount, 3, 4, "profile.exe"),
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.All,
            Actions = [action, secondAction]
        };
        AutomationProfileStore.Save(Path.Combine(ConfigDirectory, "automation-profiles.json"), new AutomationProfileDocument
        {
            ActiveProfileId = ProfileId,
            ActiveActionId = ActionId,
            RecentProfileIds = [ProfileId],
            Profiles = [profile]
        });
    }

    private static AppDefaults Defaults(
        int milliseconds,
        int repeatCount,
        int x,
        int y,
        string targetExecutable,
        uint hotkeyModifiers = 0) => new()
    {
        Milliseconds = milliseconds,
        Input = "Left",
        MouseButton = "Left",
        ClickType = "Single",
        RepeatUntilStopped = false,
        RepeatCount = repeatCount,
        FixedPosition = true,
        X = x,
        Y = y,
        TargetExecutable = targetExecutable,
        TargetWindowEnabled = true,
        Hotkey = 117,
        HotkeyModifiers = hotkeyModifiers,
        HotkeyTrigger = HotkeyTrigger.Keyboard
    };
}
