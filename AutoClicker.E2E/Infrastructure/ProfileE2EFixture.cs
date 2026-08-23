// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AutoClicker.E2E;

internal sealed class ProfileE2EFixture : IDisposable
{
    private const string TestDirectoryName = "AutoClicker.E2E";
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
        ConfigDirectory = Path.Combine(Path.GetTempPath(), TestDirectoryName, Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat));
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
        SequenceLibraryStore.Save(Path.Combine(ConfigDirectory, ConfigurationFileNames.SequenceLibrary), presets);

    internal AppDefaults ReadGlobalDefaults() =>
        JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.AdvancedSharedDefaults)))
        ?? throw new InvalidDataException("Global defaults were not valid JSON.");

    internal AppDefaults ReadSimpleDefaults() =>
        JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.SimpleDefaults)))
        ?? throw new InvalidDataException("Simple defaults were not valid JSON.");

    internal AutomationProfileDocument ReadProfiles() =>
        AutomationProfileStore.Load(Path.Combine(ConfigDirectory, ConfigurationFileNames.AutomationProfiles), new AppDefaults());

    internal IReadOnlyList<SequencePreset> ReadSequenceLibrary() =>
        SequenceLibraryStore.Load(Path.Combine(ConfigDirectory, ConfigurationFileNames.SequenceLibrary));

    internal ApplicationPreferences ReadApplicationPreferences() =>
        ApplicationPreferencesStore.Load(Path.Combine(ConfigDirectory, ConfigurationFileNames.ApplicationPreferences));

    internal void WriteApplicationPreferences(ApplicationPreferences preferences) =>
        ApplicationPreferencesStore.Save(Path.Combine(ConfigDirectory, ConfigurationFileNames.ApplicationPreferences), preferences);

    internal RgbSettings ReadRgbSettings() =>
        JsonSerializer.Deserialize<RgbSettings>(File.ReadAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.RgbSettings)))
        ?? throw new InvalidDataException("RGB settings were not valid JSON.");

    internal string ReadAppearance() =>
        File.Exists(Path.Combine(ConfigDirectory, ConfigurationFileNames.AppearanceSettings))
            ? File.ReadAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.AppearanceSettings))
            : string.Empty;

    internal IReadOnlyList<string> ReadRuntimeEvents()
    {
        var path = Path.Combine(ConfigDirectory, ConfigurationFileNames.EndToEndRuntimeLog);
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
        File.WriteAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.AdvancedSharedDefaults), JsonSerializer.Serialize(global));
        File.WriteAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.SimpleDefaults), JsonSerializer.Serialize(Defaults(50, 5, 0, 0, "simple.exe", hotkeyModifiers)));
        ApplicationPreferencesStore.Save(Path.Combine(ConfigDirectory, ConfigurationFileNames.ApplicationPreferences), new ApplicationPreferences
        {
            AdvancedMode = advancedMode,
            QuickStartSeen = true,
            KeyboardHotkeyModifiersEnabled = chordedKeyboardHotkeys,
            CrashRecoveryEnabled = false
        });
        File.WriteAllText(Path.Combine(ConfigDirectory, ConfigurationFileNames.RgbSettings), JsonSerializer.Serialize(new RgbSettings
        {
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
        AutomationProfileStore.Save(Path.Combine(ConfigDirectory, ConfigurationFileNames.AutomationProfiles), new AutomationProfileDocument
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
        Input = AutomationInputIds.Left,
        MouseButton = AutomationInputIds.Left,
        ClickType = AutomationActionTypeIds.Single,
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
