// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;

namespace AutoClicker;

// A profile export is deliberately self-contained, so it remains portable as the main config evolves.
public sealed class ProfileTransferDocument
{
    public int SchemaVersion { get; set; } = 1;
    public AutomationProfile Profile { get; set; } = new();
}

internal static class ProfileTransferStore
{
    internal const int CurrentSchemaVersion = 1;

    internal static void Save(string path, AutomationProfile profile)
    {
        var document = new ProfileTransferDocument { SchemaVersion = CurrentSchemaVersion, Profile = profile.Clone() };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ConfigurationFileNames.TemporarySuffix;
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    internal static AutomationProfile Load(string path)
    {
        var document = JsonSerializer.Deserialize<ProfileTransferDocument>(File.ReadAllText(path));
        if (document is null || document.SchemaVersion > CurrentSchemaVersion || document.Profile is null || string.IsNullOrWhiteSpace(document.Profile.Name))
            throw new InvalidDataException($"This is not a supported {AppIdentity.Name} profile export.");

        var source = document.Profile;
        var profile = new AutomationProfile
        {
            Id = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat),
            Name = source.Name.Trim(),
            BehaviorDefaults = source.BehaviorDefaults?.Clone(),
            UsesSharedBehaviorDefaults = source.UsesSharedBehaviorDefaults,
            BehaviorOverrides = source.BehaviorOverrides,
            LightingDefaults = source.LightingDefaults?.Clone(),
            Actions = (source.Actions ?? [])
            .Where(action => action.Settings is not null)
            .Select(CloneForImport)
            .ToList()
        };
        AutomationProfileLimits.Enforce(profile);
        return profile;
    }

    private static AutomationAction CloneForImport(AutomationAction action)
    {
        var clone = action.Clone();
        clone.Id = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat);
        return clone;
    }
}

public enum ProfileCopyConflictResolution
{
    Replace,
    Skip
}

internal sealed record ProfileCopyResult(int CopiedCount, int ReplacedCount, int SkippedCount);

// Keeps copy semantics independent from the WPF surface and easy to regression-test.
internal static class AutomationProfileCopy
{
    internal static AutomationProfile CreateNewProfile(string name, AutomationProfile source, IEnumerable<AutomationAction> actions)
    {
        return new AutomationProfile
        {
            Name = name.Trim(),
            BehaviorDefaults = source.BehaviorDefaults?.Clone(),
            UsesSharedBehaviorDefaults = source.UsesSharedBehaviorDefaults,
            BehaviorOverrides = source.BehaviorOverrides,
            LightingDefaults = source.LightingDefaults?.Clone(),
            Actions = actions.DistinctBy(action => action.Id).Take(AutomationProfileLimits.MaximumHotkeys).Select(CloneForCopy).ToList()
        };
    }

    internal static ProfileCopyResult CopyTo(AutomationProfile destination, IEnumerable<AutomationAction> actions, ProfileCopyConflictResolution resolution, bool keyboardModifiersEnabled = true)
    {
        var copied = 0;
        var replaced = 0;
        var skipped = 0;
        foreach (var source in actions.DistinctBy(action => action.Id))
        {
            var matches = destination.Actions.Where(action => AutomationHotkeyBindingRules.ActionsConflict(action, source, keyboardModifiersEnabled)).ToList();
            if (matches.Count > 0)
            {
                if (resolution == ProfileCopyConflictResolution.Skip)
                {
                    skipped++;
                    continue;
                }
                foreach (var match in matches) destination.Actions.Remove(match);
                replaced++;
            }
            else if (destination.Actions.Count >= AutomationProfileLimits.MaximumHotkeys)
            {
                skipped++;
                continue;
            }
            destination.Actions.Add(CloneForCopy(source));
            copied++;
        }
        return new ProfileCopyResult(copied, replaced, skipped);
    }

    private static AutomationAction CloneForCopy(AutomationAction source)
    {
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat);
        return copy;
    }
}
