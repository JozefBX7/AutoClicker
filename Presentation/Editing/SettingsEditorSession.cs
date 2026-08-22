// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AutoClicker;

internal enum SettingsEditorScopeKind
{
    Simple,
    GlobalDefaults,
    ProfileDefaults,
    Hotkey,
    MultipleHotkeys
}

internal enum SettingsEditorStorageTarget
{
    SimpleDefaults,
    GlobalDefaults,
    ProfileDefaults,
    HotkeyOverride
}

internal enum SettingsEditorTextFieldKind
{
    None,
    Interval,
    RepeatCount,
    CursorPosition,
    TargetWindow
}

internal readonly record struct SettingsEditorScope(SettingsEditorScopeKind Kind, string? TargetId = null)
{
    internal static SettingsEditorScope Simple => new(SettingsEditorScopeKind.Simple);
    internal static SettingsEditorScope GlobalDefaults => new(SettingsEditorScopeKind.GlobalDefaults);
    internal static SettingsEditorScope ProfileDefaults(string profileId) => new(SettingsEditorScopeKind.ProfileDefaults, profileId);
    internal static SettingsEditorScope Hotkey(string actionId) => new(SettingsEditorScopeKind.Hotkey, actionId);
    internal static SettingsEditorScope MultipleHotkeys => new(SettingsEditorScopeKind.MultipleHotkeys);
}

// Owns editor selection and scope as one state machine. The active profile/action in the document remains
// navigation and hotkey-registration state; it no longer has to double as an implicit description of the editor.
internal sealed class SettingsEditorSession
{
    private readonly HashSet<string> selectedActionIds = new(StringComparer.Ordinal);

    internal SettingsEditorScope Scope { get; private set; } = SettingsEditorScope.Simple;
    internal IReadOnlySet<string> SelectedActionIds => selectedActionIds;
    internal int SelectedActionCount => selectedActionIds.Count;

    internal bool IsEditingProfile(string? profileId) =>
        Scope.Kind == SettingsEditorScopeKind.ProfileDefaults && Scope.TargetId == profileId;

    internal bool IsEditingHotkey(string? actionId) =>
        Scope.Kind == SettingsEditorScopeKind.Hotkey && Scope.TargetId == actionId;

    internal SettingsEditorStorageTarget StorageTarget(bool advancedMode, string? activeProfileId, string? activeActionId)
    {
        if (!advancedMode) return SettingsEditorStorageTarget.SimpleDefaults;
        // A mode change and its UI refresh are separate WPF operations. Treat a leftover Simple scope as
        // Advanced globals during that brief transition so no event can write back into Simple defaults.
        if (Scope.Kind == SettingsEditorScopeKind.Simple) return SettingsEditorStorageTarget.GlobalDefaults;
        if (Scope.Kind == SettingsEditorScopeKind.ProfileDefaults && !IsEditingProfile(activeProfileId))
            return SettingsEditorStorageTarget.GlobalDefaults;
        if (Scope.Kind == SettingsEditorScopeKind.Hotkey && !IsEditingHotkey(activeActionId))
            return SettingsEditorStorageTarget.GlobalDefaults;
        return SettingsEditorPolicy.StorageTarget(Scope.Kind);
    }

    internal void EnterSimple()
    {
        selectedActionIds.Clear();
        Scope = SettingsEditorScope.Simple;
    }

    internal void EnterSharedDefaults(bool clearSelection = true)
    {
        if (clearSelection) selectedActionIds.Clear();
        Scope = selectedActionIds.Count > 1 ? SettingsEditorScope.MultipleHotkeys : SettingsEditorScope.GlobalDefaults;
    }

    internal void EnterProfileDefaults(string profileId)
    {
        selectedActionIds.Clear();
        Scope = SettingsEditorScope.ProfileDefaults(profileId);
    }

    internal void EnterHotkey(string actionId)
    {
        selectedActionIds.Clear();
        selectedActionIds.Add(actionId);
        Scope = SettingsEditorScope.Hotkey(actionId);
    }

    internal void ToggleHotkey(string actionId)
    {
        if (!selectedActionIds.Add(actionId)) selectedActionIds.Remove(actionId);
        Scope = selectedActionIds.Count switch
        {
            0 => SettingsEditorScope.GlobalDefaults,
            1 => SettingsEditorScope.Hotkey(selectedActionIds.Single()),
            _ => SettingsEditorScope.MultipleHotkeys
        };
    }

    internal void RemoveHotkey(string actionId)
    {
        if (!selectedActionIds.Remove(actionId)) return;
        Scope = selectedActionIds.Count switch
        {
            0 => SettingsEditorScope.GlobalDefaults,
            1 => SettingsEditorScope.Hotkey(selectedActionIds.Single()),
            _ => SettingsEditorScope.MultipleHotkeys
        };
    }
}

internal static class SettingsEditorDirtyState
{
    internal static bool IsProfileDocumentDirty(
        AutomationProfileDocument document,
        string savedConfiguration,
        string? activeProfileId,
        string? unsavedProfileId) =>
        (unsavedProfileId is not null && activeProfileId == unsavedProfileId)
        || !string.Equals(savedConfiguration, AutomationProfileConfiguration.Fingerprint(document), StringComparison.Ordinal);
}

internal static class SettingsEditorPolicy
{
    // Multiple selection displays the shared editor, so direct edits continue to target global defaults.
    internal static SettingsEditorStorageTarget StorageTarget(SettingsEditorScopeKind scope) => scope switch
    {
        SettingsEditorScopeKind.Simple => SettingsEditorStorageTarget.SimpleDefaults,
        SettingsEditorScopeKind.ProfileDefaults => SettingsEditorStorageTarget.ProfileDefaults,
        SettingsEditorScopeKind.Hotkey => SettingsEditorStorageTarget.HotkeyOverride,
        _ => SettingsEditorStorageTarget.GlobalDefaults
    };

    internal static bool ShouldCommitAndReleasePendingIntervalBeforeTransition(bool intervalHasKeyboardFocus, bool editorTransition) =>
        intervalHasKeyboardFocus && editorTransition;

    internal static bool ShouldSubmitTextField(
        SettingsEditorTextFieldKind field,
        bool enterPressed,
        bool inputCapturePending) =>
        field != SettingsEditorTextFieldKind.None && enterPressed && !inputCapturePending;

    internal static SettingsEditorScope ResolveScopeAfterDocumentReload(
        SettingsEditorScope previousScope,
        string? activeProfileId,
        IEnumerable<string> availableActionIds)
    {
        if (previousScope.Kind == SettingsEditorScopeKind.ProfileDefaults
            && previousScope.TargetId == activeProfileId)
            return previousScope;

        if (previousScope.Kind == SettingsEditorScopeKind.Hotkey
            && previousScope.TargetId is { } actionId
            && availableActionIds.Contains(actionId, StringComparer.Ordinal))
            return previousScope;

        return SettingsEditorScope.GlobalDefaults;
    }
}

internal static class SettingsEditorProfileDraft
{
    internal static bool Capture(AutomationProfile profile, AppDefaults editorDefaults, AppDefaults globalDefaults)
    {
        var overrides = profile.ActiveBehaviorOverrides;
        if (overrides == AutomationBehaviorOverride.None)
        {
            if (profile.BehaviorDefaults is null) return false;
            profile.BehaviorDefaults = null;
            return true;
        }

        var current = profile.BehaviorDefaults?.Clone() ?? globalDefaults.Clone();
        var updated = current.Clone();
        AutomationBehaviorSettingsResolver.CopyBehaviorAspects(editorDefaults, updated, overrides);
        if (JsonSerializer.Serialize(current) == JsonSerializer.Serialize(updated)) return false;
        profile.BehaviorDefaults = updated;
        return true;
    }
}
