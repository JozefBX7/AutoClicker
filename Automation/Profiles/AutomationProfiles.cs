// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;

namespace AutoClicker;

// Behavior can inherit globally while selected parts remain local to one hotkey.
[Flags]
public enum AutomationBehaviorOverride
{
    None = 0,
    Repeat = 1,
    Position = 2,
    TargetWindow = 4,
    InputJitter = 8,
    InputPulse = 16,
    Interval = 32,
    All = Repeat | Position | TargetWindow | InputJitter | InputPulse | Interval
}

// Keep capacity and compact-footer thresholds together so every import and edit path follows one rule.
internal static class AutomationProfileLimits
{
    internal const int MaximumHotkeys = 15;
    internal const int HideInlineActionControlsAt = 10;

    internal static int Enforce(AutomationProfile profile)
    {
        var excess = Math.Max(0, profile.Actions.Count - MaximumHotkeys);
        if (excess > 0) profile.Actions.RemoveRange(MaximumHotkeys, excess);
        return excess;
    }

    internal static void Enforce(AutomationProfileDocument document)
    {
        foreach (var profile in document.Profiles) Enforce(profile);
    }
}

// Profile names are user-facing identifiers, so all creation, import, and migration paths share one case-insensitive rule.
internal static class AutomationProfileNameRules
{
    internal static string MakeUnique(string? preferredName, IEnumerable<AutomationProfile> profiles, string? excludedProfileId = null) =>
        MakeUnique(preferredName, profiles.Where(profile => profile.Id != excludedProfileId).Select(profile => profile.Name));

    internal static bool EnsureUnique(IEnumerable<AutomationProfile> profiles)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var profile in profiles)
        {
            var unique = MakeUnique(profile.Name, used);
            if (!string.Equals(profile.Name, unique, StringComparison.Ordinal))
            {
                profile.Name = unique;
                changed = true;
            }
            used.Add(unique);
        }
        return changed;
    }

    private static string MakeUnique(string? preferredName, IEnumerable<string> existingNames)
    {
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? "Profile" : preferredName.Trim();
        var used = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}

// A profile is a named set of independently toggleable automation actions.
public sealed class AutomationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    // Null keeps compatibility with profiles that inherit the Advanced-mode fallback defaults.
    public AppDefaults? BehaviorDefaults { get; set; }
    // Profiles use the same per-section inheritance model as hotkeys. A null defaults object means every
    // behavior aspect comes directly from the global Advanced defaults.
    public bool UsesSharedBehaviorDefaults { get; set; }
    public AutomationBehaviorOverride BehaviorOverrides { get; set; }
    // Null inherits the app-wide lighting settings; hotkeys can still override this profile value.
    public RgbSettings? LightingDefaults { get; set; }
    public List<AutomationAction> Actions { get; set; } = [];

    public bool UsesSharedBehavior(AutomationBehaviorOverride aspect) => BehaviorDefaults is null || (UsesSharedBehaviorDefaults && !BehaviorOverrides.HasFlag(aspect));
    public AutomationBehaviorOverride ActiveBehaviorOverrides => BehaviorDefaults is null ? AutomationBehaviorOverride.None : UsesSharedBehaviorDefaults ? BehaviorOverrides : AutomationBehaviorOverride.All;

    public AutomationProfile Clone() => new() { Id = Id, Name = Name, BehaviorDefaults = BehaviorDefaults?.Clone(), UsesSharedBehaviorDefaults = UsesSharedBehaviorDefaults, BehaviorOverrides = BehaviorOverrides, LightingDefaults = LightingDefaults?.Clone(), Actions = Actions.Select(action => action.Clone()).ToList() };
}

public sealed class AutomationAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AppDefaults Settings { get; set; } = new();
    // Existing actions retain their own values; newly added actions can opt into shared defaults.
    public bool UsesSharedBehaviorDefaults { get; set; }
    public AutomationBehaviorOverride BehaviorOverrides { get; set; }
    // Disabling a hotkey leaves its action available for the footer controls without registering its trigger.
    public bool HotkeyEnabled { get; set; } = true;
    public bool UsesSharedLightingSettings { get; set; } = true;
    public RgbSettings? LightingOverride { get; set; }
    public bool UsesSharedBehavior(AutomationBehaviorOverride aspect) => UsesSharedBehaviorDefaults && !BehaviorOverrides.HasFlag(aspect);
    public AutomationBehaviorOverride ActiveBehaviorOverrides => UsesSharedBehaviorDefaults ? BehaviorOverrides : AutomationBehaviorOverride.All;
    public bool MatchesHotkey(int hotkey, uint modifiers, HotkeyTrigger trigger) =>
        HotkeyFormatter.IsConfigured(Settings.Hotkey, Settings.HotkeyTrigger)
        && Settings.Hotkey == hotkey
        && Settings.HotkeyModifiers == modifiers
        && Settings.HotkeyTrigger == trigger;
    public AutomationAction Clone() => new() { Id = Id, Settings = Settings.Clone(), UsesSharedBehaviorDefaults = UsesSharedBehaviorDefaults, BehaviorOverrides = BehaviorOverrides, HotkeyEnabled = HotkeyEnabled, UsesSharedLightingSettings = UsesSharedLightingSettings, LightingOverride = LightingOverride?.Clone() };
    public string DisplayName => $"{HotkeyFormatter.Format(Settings.Hotkey, Settings.HotkeyModifiers, Settings.HotkeyTrigger)}  ·  {ActionDescription}";
    public string ActionDescription => Describe(Settings);

    private static string Describe(AppDefaults settings)
    {
        var input = settings.Input switch
        {
            "Unset" => "Set action",
            "Space" => "Space",
            "Enter" => "Enter",
            "Custom" when settings.CustomKey != 0 => System.Windows.Input.KeyInterop.KeyFromVirtualKey(settings.CustomKey).ToString(),
            "Sequence" => "Custom sequence",
            "Right" => "Right click",
            "Middle" => "Middle click",
            "Left" => "Left click",
            _ => string.IsNullOrWhiteSpace(settings.MouseButton) || settings.MouseButton == "Unset" ? "Set action" : settings.MouseButton + " click"
        };
        var typedInput = input.EndsWith(" click", StringComparison.Ordinal) ? char.ToLowerInvariant(input[0]) + input[1..] : input;
        return input == "Set action" || settings.Input == "Sequence" ? input : settings.ClickType switch
        {
            "Double" => $"Double {typedInput}",
            "Hold" => $"Hold {typedInput}",
            _ => input
        };
    }

}

public sealed class AutomationProfileDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveProfileId { get; set; } = string.Empty;
    public string ActiveActionId { get; set; } = string.Empty;
    // Newest first. This keeps the footer useful without making profile selection noisy.
    public List<string> RecentProfileIds { get; set; } = [];
    public List<AutomationProfile> Profiles { get; set; } = [];
}

internal static class AutomationProfileStore
{
    internal const int CurrentSchemaVersion = 1;

    internal static AutomationProfileDocument Load(string path, AppDefaults fallback)
    {
        try
        {
            if (!File.Exists(path)) return CreateInitial(fallback);
            var document = JsonSerializer.Deserialize<AutomationProfileDocument>(File.ReadAllText(path));
            if (document is null || document.SchemaVersion > CurrentSchemaVersion) return CreateInitial(fallback);
            document.Profiles = document.Profiles.Where(profile => !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.Name))
                .Select(profile => new AutomationProfile { Id = profile.Id, Name = profile.Name.Trim(), BehaviorDefaults = profile.BehaviorDefaults?.Clone(), UsesSharedBehaviorDefaults = profile.UsesSharedBehaviorDefaults, BehaviorOverrides = profile.BehaviorOverrides, LightingDefaults = profile.LightingDefaults?.Clone(), Actions = profile.Actions.Where(action => !string.IsNullOrWhiteSpace(action.Id)).Select(action => action.Clone()).ToList() }).ToList();
            if (document.Profiles.Count == 0) return CreateInitial(fallback);
            AutomationProfileNameRules.EnsureUnique(document.Profiles);
            document.RecentProfileIds = document.RecentProfileIds
                .Where(id => document.Profiles.Any(profile => profile.Id == id)).Distinct().ToList();
            if (!string.IsNullOrWhiteSpace(document.ActiveProfileId))
                document.RecentProfileIds.Remove(document.ActiveProfileId);
            if (!string.IsNullOrWhiteSpace(document.ActiveProfileId))
                document.RecentProfileIds.Insert(0, document.ActiveProfileId);
            AutomationProfileLimits.Enforce(document);
            return document;
        }
        catch { return CreateInitial(fallback); }
    }

    internal static void Save(string path, AutomationProfileDocument document)
    {
        AutomationProfileLimits.Enforce(document);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    internal static AutomationProfileDocument CreateInitial(AppDefaults fallback)
    {
        var action = new AutomationAction { Settings = fallback.Clone() };
        var profile = new AutomationProfile { Name = "General", Actions = [action] };
        return new AutomationProfileDocument { Profiles = [profile], ActiveProfileId = profile.Id, ActiveActionId = action.Id, RecentProfileIds = [profile.Id] };
    }
}

// Navigation state is intentionally excluded: only saved profile content should enable the Save button.
internal static class AutomationProfileConfiguration
{
    internal static string Fingerprint(AutomationProfileDocument document) => JsonSerializer.Serialize(document.Profiles);
}

// A blank draft is only a temporary canvas; it should not interrupt normal profile navigation.
internal static class AutomationProfileDraftRules
{
    internal static bool HasContent(AutomationProfile profile) => profile.Actions.Count > 0 || profile.BehaviorDefaults is not null;
}

// Profile action order is persisted as list order, so this remains independent from the WPF drag surface.
internal static class AutomationProfileActionOrder
{
    internal static bool Move(AutomationProfile profile, string actionId, string targetActionId, bool placeAfter)
    {
        if (actionId == targetActionId) return false;
        var sourceIndex = profile.Actions.FindIndex(action => action.Id == actionId);
        var targetIndex = profile.Actions.FindIndex(action => action.Id == targetActionId);
        if (sourceIndex < 0 || targetIndex < 0) return false;

        var action = profile.Actions[sourceIndex];
        profile.Actions.RemoveAt(sourceIndex);
        targetIndex = profile.Actions.FindIndex(item => item.Id == targetActionId);
        var destinationIndex = targetIndex + (placeAfter ? 1 : 0);
        if (destinationIndex == sourceIndex) { profile.Actions.Insert(sourceIndex, action); return false; }
        profile.Actions.Insert(destinationIndex, action);
        return true;
    }
}

// Input identity and the hotkey remain properties of each action; behavior and interval can inherit.
internal static class AutomationBehaviorSettingsResolver
{
    internal static AppDefaults ResolveProfileDefaults(AppDefaults globalDefaults, AutomationProfile? profile)
    {
        var settings = globalDefaults.Clone();
        if (profile?.BehaviorDefaults is not { } local) return settings;
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.Interval))
        {
            settings.Hours = local.Hours;
            settings.Minutes = local.Minutes;
            settings.Seconds = local.Seconds;
            settings.Milliseconds = local.Milliseconds;
        }
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.Repeat))
        {
            settings.RepeatUntilStopped = local.RepeatUntilStopped;
            settings.RepeatCount = local.RepeatCount;
        }
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.Position))
        {
            settings.FixedPosition = local.FixedPosition;
            settings.X = local.X;
            settings.Y = local.Y;
        }
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow))
        {
            settings.TargetExecutable = local.TargetExecutable;
            settings.TargetWindowTitle = local.TargetWindowTitle;
            settings.TargetWindowEnabled = local.TargetWindowEnabled;
        }
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter)) settings.InputJitterMaximumMilliseconds = local.InputJitterMaximumMilliseconds;
        if (!profile.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse)) settings.InputPulseMilliseconds = local.InputPulseMilliseconds;
        return settings;
    }

    internal static AppDefaults Resolve(AppDefaults globalDefaults, AutomationProfile? profile, AutomationAction action)
    {
        var settings = action.Settings.Clone();
        var inherited = ResolveProfileDefaults(globalDefaults, profile);

        if (action.UsesSharedBehavior(AutomationBehaviorOverride.Interval))
        {
            settings.Hours = inherited.Hours;
            settings.Minutes = inherited.Minutes;
            settings.Seconds = inherited.Seconds;
            settings.Milliseconds = inherited.Milliseconds;
        }
        if (action.UsesSharedBehavior(AutomationBehaviorOverride.Repeat))
        {
            settings.RepeatUntilStopped = inherited.RepeatUntilStopped;
            settings.RepeatCount = inherited.RepeatCount;
        }
        if (action.UsesSharedBehavior(AutomationBehaviorOverride.Position))
        {
            settings.FixedPosition = inherited.FixedPosition;
            settings.X = inherited.X;
            settings.Y = inherited.Y;
        }
        if (action.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow))
        {
            settings.TargetExecutable = inherited.TargetExecutable;
            settings.TargetWindowTitle = inherited.TargetWindowTitle;
            settings.TargetWindowEnabled = inherited.TargetWindowEnabled;
        }
        if (action.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter)) settings.InputJitterMaximumMilliseconds = inherited.InputJitterMaximumMilliseconds;
        if (action.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse)) settings.InputPulseMilliseconds = inherited.InputPulseMilliseconds;
        return settings;
    }

    // Inherited aspects must also be restored in the stored settings. Otherwise an old, inactive local value
    // makes the profile look dirty after a user overrides a value and then immediately reverts it.
    internal static void RevertActionBehaviorToInherited(AppDefaults globalDefaults, AutomationProfile? profile, AutomationAction action, AutomationBehaviorOverride reverted)
    {
        var existingOverrides = action.ActiveBehaviorOverrides;
        var restored = existingOverrides & reverted;
        if (restored == AutomationBehaviorOverride.None) return;
        var preview = action.Clone();
        preview.UsesSharedBehaviorDefaults = true;
        preview.BehaviorOverrides = existingOverrides & ~restored;
        var inherited = Resolve(globalDefaults, profile, preview);

        CopyBehaviorAspects(inherited, action.Settings, restored);
        action.UsesSharedBehaviorDefaults = true;
        action.BehaviorOverrides = existingOverrides & ~restored;
    }

    internal static void CopyBehaviorAspects(AppDefaults source, AppDefaults destination, AutomationBehaviorOverride aspects)
    {
        if (aspects.HasFlag(AutomationBehaviorOverride.Interval))
        {
            destination.Hours = source.Hours;
            destination.Minutes = source.Minutes;
            destination.Seconds = source.Seconds;
            destination.Milliseconds = source.Milliseconds;
        }
        if (aspects.HasFlag(AutomationBehaviorOverride.Repeat))
        {
            destination.RepeatUntilStopped = source.RepeatUntilStopped;
            destination.RepeatCount = source.RepeatCount;
        }
        if (aspects.HasFlag(AutomationBehaviorOverride.Position))
        {
            destination.FixedPosition = source.FixedPosition;
            destination.X = source.X;
            destination.Y = source.Y;
        }
        if (aspects.HasFlag(AutomationBehaviorOverride.TargetWindow))
        {
            destination.TargetExecutable = source.TargetExecutable;
            destination.TargetWindowTitle = source.TargetWindowTitle;
            destination.TargetWindowEnabled = source.TargetWindowEnabled;
        }
        if (aspects.HasFlag(AutomationBehaviorOverride.InputJitter)) destination.InputJitterMaximumMilliseconds = source.InputJitterMaximumMilliseconds;
        if (aspects.HasFlag(AutomationBehaviorOverride.InputPulse)) destination.InputPulseMilliseconds = source.InputPulseMilliseconds;
    }
}

internal static class AutomationLightingSettingsResolver
{
    internal static RgbSettings Resolve(RgbSettings appDefaults, AutomationProfile? profile, AutomationAction action) =>
        !action.UsesSharedLightingSettings && action.LightingOverride is not null
            ? action.LightingOverride
            : profile?.LightingDefaults ?? appDefaults;
}

public sealed class AppDefaults
{
    public int Hours { get; set; } public int Minutes { get; set; } public int Seconds { get; set; } public int Milliseconds { get; set; } = 100;
    public string MouseButton { get; set; } = "Left"; public string? Input { get; set; } public int CustomKey { get; set; }
    public List<SequenceStep>? CustomSequence { get; set; } public bool CustomSequenceUsesGlobalInputPulse { get; set; } = true;
    public string ClickType { get; set; } = "Single"; public bool RepeatUntilStopped { get; set; } = true; public int RepeatCount { get; set; } = 10;
    public bool FixedPosition { get; set; } public int X { get; set; } public int Y { get; set; }
    public int? InputPulseMilliseconds { get; set; } = InputRules.DefaultInputPulseMilliseconds; public long InputJitterMaximumMilliseconds { get; set; }
    public string TargetExecutable { get; set; } = string.Empty; public string? TargetWindowTitle { get; set; } public bool TargetWindowEnabled { get; set; } = true;
    public int Hotkey { get; set; } = 117; public uint HotkeyModifiers { get; set; }
    // Mouse triggers are stored separately from virtual keys, so older keyboard-only settings remain valid.
    public HotkeyTrigger HotkeyTrigger { get; set; } = HotkeyTrigger.Keyboard;
    public RgbSettings? Rgb { get; set; }
    public AppDefaults Clone() => new()
    {
        Hours = Hours, Minutes = Minutes, Seconds = Seconds, Milliseconds = Milliseconds, MouseButton = MouseButton, Input = Input, CustomKey = CustomKey,
        CustomSequence = CustomSequence?.Select(step => step.Clone()).ToList(), CustomSequenceUsesGlobalInputPulse = CustomSequenceUsesGlobalInputPulse, ClickType = ClickType,
        RepeatUntilStopped = RepeatUntilStopped, RepeatCount = RepeatCount, FixedPosition = FixedPosition, X = X, Y = Y, InputPulseMilliseconds = InputPulseMilliseconds,
        InputJitterMaximumMilliseconds = InputJitterMaximumMilliseconds, TargetExecutable = TargetExecutable, TargetWindowTitle = TargetWindowTitle, TargetWindowEnabled = TargetWindowEnabled,
        Hotkey = Hotkey, HotkeyModifiers = HotkeyModifiers, HotkeyTrigger = HotkeyTrigger, Rgb = Rgb
    };
}
