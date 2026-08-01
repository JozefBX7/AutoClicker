using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.Windows;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AutomationProfileTests
{
    [TestMethod]
    public void MissingStore_CreatesReadyToUseGeneralProfile()
    {
        var path = TemporaryPath();
        try
        {
            var document = AutomationProfileStore.Load(path, new AppDefaults { Input = "Left", Hotkey = 117 });
            Assert.AreEqual(1, document.Profiles.Count);
            Assert.AreEqual("General", document.Profiles[0].Name);
            Assert.AreEqual(1, document.Profiles[0].Actions.Count);
            Assert.AreEqual(117, document.Profiles[0].Actions[0].Settings.Hotkey);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void ProfileTransfer_RoundTripsUnselectedAction()
    {
        var path = TemporaryPath();
        try
        {
            var profile = new AutomationProfile
            {
                Name = "Unselected action",
                Actions = [new AutomationAction { Settings = new AppDefaults { Hotkey = 118, Input = "Unset", MouseButton = "Unset" } }]
            };

            ProfileTransferStore.Save(path, profile);
            var imported = ProfileTransferStore.Load(path);

            Assert.AreEqual("Unset", imported.Actions[0].Settings.Input);
            Assert.AreEqual("Unset", imported.Actions[0].Settings.MouseButton);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void MissingAdvancedProfileStore_UsesItsOwnFallbackRatherThanSimpleModeValues()
    {
        var path = TemporaryPath();
        try
        {
            var simpleModeSettings = new AppDefaults { Milliseconds = 17, InputPulseMilliseconds = 5, InputJitterMaximumMilliseconds = 12 };
            var advancedDefaults = new AppDefaults { Milliseconds = 100, InputPulseMilliseconds = 2, InputJitterMaximumMilliseconds = 0 };

            var document = AutomationProfileStore.Load(path, advancedDefaults);
            var initial = document.Profiles[0].Actions[0].Settings;

            Assert.AreEqual(100, initial.Milliseconds);
            Assert.AreEqual(2, initial.InputPulseMilliseconds);
            Assert.AreEqual(0L, initial.InputJitterMaximumMilliseconds);
            Assert.AreNotEqual(simpleModeSettings.Milliseconds, initial.Milliseconds);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void ProfileNames_AreUniqueCaseInsensitivelyAcrossLoadAndCreation()
    {
        var path = TemporaryPath();
        try
        {
            var document = new AutomationProfileDocument
            {
                Profiles = [new AutomationProfile { Id = "one", Name = "Games" }, new AutomationProfile { Id = "two", Name = "games" }, new AutomationProfile { Id = "three", Name = "Tools" }]
            };
            AutomationProfileStore.Save(path, document);

            var loaded = AutomationProfileStore.Load(path, new AppDefaults());

            CollectionAssert.AreEqual(new[] { "Games", "games (2)", "Tools" }, loaded.Profiles.Select(profile => profile.Name).ToArray());
            Assert.AreEqual("games (3)", AutomationProfileNameRules.MakeUnique("games", loaded.Profiles));
            Assert.AreEqual("games", AutomationProfileNameRules.MakeUnique("games", loaded.Profiles, "one"));
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void Store_RoundTripsMultipleActionsAndTargetRules()
    {
        var path = TemporaryPath();
        try
        {
            var action = new AutomationAction { Id = "hold-e", Settings = new AppDefaults { Input = "Custom", CustomKey = 69, ClickType = "Hold", Hotkey = 118, TargetExecutable = "game.exe", TargetWindowTitle = "Game", TargetWindowEnabled = true, HotkeyTrigger = HotkeyTrigger.Mouse4 } };
            var profileDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 8, FixedPosition = true, X = 120, Y = 340, InputJitterMaximumMilliseconds = 4 };
            var document = new AutomationProfileDocument { ActiveProfileId = "general", ActiveActionId = action.Id, Profiles = [new AutomationProfile { Id = "general", Name = "General", BehaviorDefaults = profileDefaults, Actions = [action] }] };
            AutomationProfileStore.Save(path, document);
            var loaded = AutomationProfileStore.Load(path, new AppDefaults());
            var settings = loaded.Profiles[0].Actions[0].Settings;
            Assert.AreEqual("game.exe", settings.TargetExecutable);
            Assert.AreEqual("Game", settings.TargetWindowTitle);
            Assert.AreEqual(69, settings.CustomKey);
            Assert.AreEqual(118, settings.Hotkey);
            Assert.AreEqual(HotkeyTrigger.Mouse4, settings.HotkeyTrigger);
            Assert.IsNotNull(loaded.Profiles[0].BehaviorDefaults);
            Assert.IsFalse(loaded.Profiles[0].BehaviorDefaults!.RepeatUntilStopped);
            Assert.AreEqual(8, loaded.Profiles[0].BehaviorDefaults!.RepeatCount);
            Assert.IsTrue(loaded.Profiles[0].BehaviorDefaults!.FixedPosition);
            Assert.AreEqual(4L, loaded.Profiles[0].BehaviorDefaults!.InputJitterMaximumMilliseconds);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void Profile_ClonePreservesIndependentBehaviorDefaults()
    {
        var profile = new AutomationProfile
        {
            BehaviorDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 3 },
            Actions = [new AutomationAction()]
        };

        var clone = profile.Clone();
        clone.BehaviorDefaults!.RepeatCount = 9;

        Assert.AreEqual(3, profile.BehaviorDefaults!.RepeatCount);
        Assert.AreEqual(9, clone.BehaviorDefaults!.RepeatCount);
    }

    [TestMethod]
    public void Store_KeepsOnlyValidRecentProfilesAndPutsActiveFirst()
    {
        var path = TemporaryPath();
        try
        {
            var general = new AutomationProfile { Id = "general", Name = "General", Actions = [new AutomationAction()] };
            var games = new AutomationProfile { Id = "games", Name = "Games", Actions = [new AutomationAction()] };
            var document = new AutomationProfileDocument
            {
                ActiveProfileId = games.Id,
                ActiveActionId = games.Actions[0].Id,
                RecentProfileIds = ["missing", general.Id, general.Id],
                Profiles = [general, games]
            };
            AutomationProfileStore.Save(path, document);

            var loaded = AutomationProfileStore.Load(path, new AppDefaults());

            CollectionAssert.AreEqual(new[] { games.Id, general.Id }, loaded.RecentProfileIds);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void Store_PreservesAnEmptyProfile()
    {
        var path = TemporaryPath();
        try
        {
            var empty = new AutomationProfile { Id = "empty", Name = "Empty" };
            AutomationProfileStore.Save(path, new AutomationProfileDocument { ActiveProfileId = empty.Id, Profiles = [empty] });

            var loaded = AutomationProfileStore.Load(path, new AppDefaults());

            Assert.AreEqual("Empty", loaded.Profiles[0].Name);
            Assert.AreEqual(0, loaded.Profiles[0].Actions.Count);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void ProfileConfigurationFingerprint_IgnoresNavigationButTracksActions()
    {
        var profile = new AutomationProfile { Id = "general", Name = "General", Actions = [new AutomationAction { Id = "f6" }] };
        var original = new AutomationProfileDocument { ActiveProfileId = profile.Id, ActiveActionId = "f6", RecentProfileIds = [profile.Id], Profiles = [profile] };
        var afterNavigation = new AutomationProfileDocument { ActiveProfileId = "other", ActiveActionId = "other-action", RecentProfileIds = ["other", profile.Id], Profiles = [profile.Clone()] };

        Assert.AreEqual(AutomationProfileConfiguration.Fingerprint(original), AutomationProfileConfiguration.Fingerprint(afterNavigation));

        afterNavigation.Profiles[0].Actions[0].Settings.Hotkey = 118;
        Assert.AreNotEqual(AutomationProfileConfiguration.Fingerprint(original), AutomationProfileConfiguration.Fingerprint(afterNavigation));

        afterNavigation.Profiles[0].Actions[0].Settings.Hotkey = 117;
        afterNavigation.Profiles[0].BehaviorDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 4 };
        Assert.AreNotEqual(AutomationProfileConfiguration.Fingerprint(original), AutomationProfileConfiguration.Fingerprint(afterNavigation));
    }

    [TestMethod]
    public void Action_ClonePreservesSharedBehaviorMode()
    {
        var action = new AutomationAction { UsesSharedBehaviorDefaults = true, BehaviorOverrides = AutomationBehaviorOverride.Position, Settings = new AppDefaults { Hotkey = 118 } };

        var copy = action.Clone();

        Assert.IsTrue(copy.UsesSharedBehaviorDefaults);
        Assert.AreEqual(AutomationBehaviorOverride.Position, copy.BehaviorOverrides);
        Assert.AreEqual(118, copy.Settings.Hotkey);
        Assert.AreNotSame(action.Settings, copy.Settings);
    }

    [TestMethod]
    public void Action_CanOverrideOneBehaviorAspectWhileUsingTheRestOfTheSharedDefaults()
    {
        var action = new AutomationAction { UsesSharedBehaviorDefaults = true, BehaviorOverrides = AutomationBehaviorOverride.Position | AutomationBehaviorOverride.InputPulse };

        Assert.IsTrue(action.UsesSharedBehavior(AutomationBehaviorOverride.Repeat));
        Assert.IsFalse(action.UsesSharedBehavior(AutomationBehaviorOverride.Position));
        Assert.IsFalse(action.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse));
        Assert.AreEqual(AutomationBehaviorOverride.Position | AutomationBehaviorOverride.InputPulse, action.ActiveBehaviorOverrides);
    }

    [TestMethod]
    public void SharedBehavior_UsesProfileTimingDefaultsBeforeGlobalDefaults()
    {
        var global = new AppDefaults { InputPulseMilliseconds = 1, InputJitterMaximumMilliseconds = 8 };
        var profile = new AutomationProfile { BehaviorDefaults = new AppDefaults { InputPulseMilliseconds = 3, InputJitterMaximumMilliseconds = 4 } };
        var action = new AutomationAction
        {
            UsesSharedBehaviorDefaults = true,
            Settings = new AppDefaults { InputPulseMilliseconds = 5, InputJitterMaximumMilliseconds = 12 }
        };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile, action);

        Assert.AreEqual(3, resolved.InputPulseMilliseconds);
        Assert.AreEqual(4L, resolved.InputJitterMaximumMilliseconds);
    }

    [TestMethod]
    public void HotkeyTimingOverride_TakesPrecedenceOverProfileAndGlobalDefaults()
    {
        var global = new AppDefaults { InputPulseMilliseconds = 1, InputJitterMaximumMilliseconds = 8 };
        var profile = new AutomationProfile { BehaviorDefaults = new AppDefaults { InputPulseMilliseconds = 3, InputJitterMaximumMilliseconds = 4 } };
        var action = new AutomationAction
        {
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.InputPulse | AutomationBehaviorOverride.InputJitter,
            Settings = new AppDefaults { InputPulseMilliseconds = 5, InputJitterMaximumMilliseconds = 12 }
        };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile, action);

        Assert.AreEqual(5, resolved.InputPulseMilliseconds);
        Assert.AreEqual(12L, resolved.InputJitterMaximumMilliseconds);
    }

    [TestMethod]
    public void SharedBehavior_FallsBackToGlobalTimingDefaultsWithoutProfileDefaults()
    {
        var global = new AppDefaults { InputPulseMilliseconds = 4, InputJitterMaximumMilliseconds = 9 };
        var action = new AutomationAction { UsesSharedBehaviorDefaults = true };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile: null, action: action);

        Assert.AreEqual(4, resolved.InputPulseMilliseconds);
        Assert.AreEqual(9L, resolved.InputJitterMaximumMilliseconds);
    }

    [TestMethod]
    public void SharedBehavior_UsesProfileDefaultsForEveryInheritedBehaviorAspect()
    {
        var global = new AppDefaults { RepeatUntilStopped = true, FixedPosition = false, TargetExecutable = "global.exe", TargetWindowEnabled = true };
        var profile = new AutomationProfile
        {
            BehaviorDefaults = new AppDefaults
            {
                RepeatUntilStopped = false,
                RepeatCount = 6,
                FixedPosition = true,
                X = 51,
                Y = 62,
                TargetExecutable = "profile.exe",
                TargetWindowTitle = "Profile target",
                TargetWindowEnabled = false,
                InputPulseMilliseconds = 3,
                InputJitterMaximumMilliseconds = 7,
                Hours = 1,
                Milliseconds = 25
            }
        };
        var action = new AutomationAction
        {
            UsesSharedBehaviorDefaults = true,
            Settings = new AppDefaults { Hours = 1, Milliseconds = 25, Input = "Space", Hotkey = 118 }
        };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile, action);

        Assert.IsFalse(resolved.RepeatUntilStopped);
        Assert.AreEqual(6, resolved.RepeatCount);
        Assert.IsTrue(resolved.FixedPosition);
        Assert.AreEqual(51, resolved.X);
        Assert.AreEqual(62, resolved.Y);
        Assert.AreEqual("profile.exe", resolved.TargetExecutable);
        Assert.AreEqual("Profile target", resolved.TargetWindowTitle);
        Assert.IsFalse(resolved.TargetWindowEnabled);
        Assert.AreEqual(3, resolved.InputPulseMilliseconds);
        Assert.AreEqual(7L, resolved.InputJitterMaximumMilliseconds);
        Assert.AreEqual(1, resolved.Hours);
        Assert.AreEqual(25, resolved.Milliseconds);
        Assert.AreEqual("Space", resolved.Input);
        Assert.AreEqual(118, resolved.Hotkey);
    }

    [TestMethod]
    public void ProfileBehaviorOverrides_CanOverrideOneSectionWhileOtherSectionsUseGlobalDefaults()
    {
        var global = new AppDefaults { RepeatUntilStopped = true, FixedPosition = false, InputPulseMilliseconds = 1 };
        var profile = new AutomationProfile
        {
            BehaviorDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 7, FixedPosition = true, InputPulseMilliseconds = 5 },
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.Repeat
        };
        var action = new AutomationAction { UsesSharedBehaviorDefaults = true };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile, action);

        Assert.IsFalse(resolved.RepeatUntilStopped);
        Assert.AreEqual(7, resolved.RepeatCount);
        Assert.IsFalse(resolved.FixedPosition);
        Assert.AreEqual(1, resolved.InputPulseMilliseconds);
        Assert.IsTrue(profile.UsesSharedBehavior(AutomationBehaviorOverride.Position));
        Assert.IsFalse(profile.UsesSharedBehavior(AutomationBehaviorOverride.Repeat));
    }

    [TestMethod]
    public void ProfileWithoutOverrides_UsesGlobalDefaultsAndLegacyProfileDefaultsRemainLocal()
    {
        var global = new AppDefaults { InputJitterMaximumMilliseconds = 2 };
        var inheritedProfile = new AutomationProfile();
        var legacyProfile = new AutomationProfile { BehaviorDefaults = new AppDefaults { InputJitterMaximumMilliseconds = 8 } };
        var action = new AutomationAction { UsesSharedBehaviorDefaults = true };

        Assert.AreEqual(2L, AutomationBehaviorSettingsResolver.Resolve(global, inheritedProfile, action).InputJitterMaximumMilliseconds);
        Assert.AreEqual(8L, AutomationBehaviorSettingsResolver.Resolve(global, legacyProfile, action).InputJitterMaximumMilliseconds);
        Assert.AreEqual(AutomationBehaviorOverride.None, inheritedProfile.ActiveBehaviorOverrides);
        Assert.AreEqual(AutomationBehaviorOverride.All, legacyProfile.ActiveBehaviorOverrides);
    }

    [TestMethod]
    public void LightingInheritance_UsesProfileDefaultsBeforeAppAndHotkeyOverrides()
    {
        var appLighting = new RgbSettings { Enabled = true, IndicatorColor = "#112233" };
        var profile = new AutomationProfile { LightingDefaults = new RgbSettings { Enabled = true, IndicatorColor = "#445566" } };
        var inheritedAction = new AutomationAction { UsesSharedLightingSettings = true };
        var overriddenAction = new AutomationAction
        {
            UsesSharedLightingSettings = false,
            LightingOverride = new RgbSettings { Enabled = true, IndicatorColor = "#778899" }
        };

        Assert.AreEqual("#445566", AutomationLightingSettingsResolver.Resolve(appLighting, profile, inheritedAction).IndicatorColor);
        Assert.AreEqual("#778899", AutomationLightingSettingsResolver.Resolve(appLighting, profile, overriddenAction).IndicatorColor);
        Assert.AreEqual("#112233", AutomationLightingSettingsResolver.Resolve(appLighting, profile: null, action: inheritedAction).IndicatorColor);
    }

    [TestMethod]
    public void HotkeyBehaviorOverrides_KeepLocalValuesWhileOtherAspectsStillInherit()
    {
        var global = new AppDefaults { RepeatUntilStopped = true, InputPulseMilliseconds = 1 };
        var profile = new AutomationProfile { BehaviorDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 2, InputPulseMilliseconds = 3 } };
        var action = new AutomationAction
        {
            UsesSharedBehaviorDefaults = true,
            BehaviorOverrides = AutomationBehaviorOverride.Repeat,
            Settings = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 11, InputPulseMilliseconds = 5 }
        };

        var resolved = AutomationBehaviorSettingsResolver.Resolve(global, profile, action);

        Assert.IsFalse(resolved.RepeatUntilStopped);
        Assert.AreEqual(11, resolved.RepeatCount);
        Assert.AreEqual(3, resolved.InputPulseMilliseconds);
    }

    [TestMethod]
    public void Action_ClonePreservesMouseHotkeyTrigger()
    {
        var action = new AutomationAction { HotkeyEnabled = false, Settings = new AppDefaults { Hotkey = 0, HotkeyTrigger = HotkeyTrigger.WheelUp } };

        var copy = action.Clone();

        Assert.AreEqual(HotkeyTrigger.WheelUp, copy.Settings.HotkeyTrigger);
        Assert.IsFalse(copy.HotkeyEnabled);
        Assert.AreEqual("Wheel up", copy.DisplayName.Split('·')[0].Trim());
    }

    [TestMethod]
    public void Action_HotkeyMatchingIncludesTheTriggerAndModifiers()
    {
        var action = new AutomationAction { Settings = new AppDefaults { Hotkey = 118, HotkeyModifiers = 2, HotkeyTrigger = HotkeyTrigger.Keyboard } };

        Assert.IsTrue(action.MatchesHotkey(118, 2, HotkeyTrigger.Keyboard));
        Assert.IsFalse(action.MatchesHotkey(118, 0, HotkeyTrigger.Keyboard));
        Assert.IsFalse(action.MatchesHotkey(0, 2, HotkeyTrigger.Mouse4));
    }

    [TestMethod]
    public void AdvancedActionTile_SeparatesHotkeyAndActionLabels()
    {
        var action = new AutomationAction
        {
            Settings = new AppDefaults { Hotkey = 117, Input = "Right" },
            UsesSharedBehaviorDefaults = false
        };

        var tile = new AdvancedActionTile(action, isRunning: false, removalPending: true, isSelected: true, isManagementLocked: true);

        Assert.AreEqual("F6", tile.HotkeyLabel);
        Assert.AreEqual("Hotkey: F6", tile.HotkeyTooltip);
        Assert.AreEqual("Right click", tile.ActionLabel);
        Assert.AreEqual(Visibility.Collapsed, tile.RemoveButtonVisibility);
        Assert.AreEqual(Visibility.Visible, tile.RemovalConfirmationVisibility);
        Assert.AreEqual(Visibility.Collapsed, tile.BehaviorBadgeVisibility);
        Assert.AreEqual(string.Empty, tile.BehaviorBadge);
        Assert.IsTrue(tile.IsSelected);
        Assert.IsFalse(tile.CanEdit);
        Assert.IsFalse(tile.CanStart);
    }

    [TestMethod]
    public void AdvancedActionTile_ShowsWaitingLabelWhileHotkeyCaptureIsPending()
    {
        var tile = new AdvancedActionTile(new AutomationAction(), isRunning: false, removalPending: false, isSelected: true, isManagementLocked: false, hotkeyCapturePending: true);

        Assert.AreEqual("Waiting...", tile.HotkeyLabel);
    }

    [TestMethod]
    public void AdvancedProfileTile_ShowsUnsavedIndicatorOnlyForPendingChanges()
    {
        var profile = new AutomationProfile { Name = "Unsaved" };

        var dirty = new AdvancedProfileTile(profile, isSelected: true, hasUnsavedChanges: true);
        var clean = new AdvancedProfileTile(profile, isSelected: true, hasUnsavedChanges: false);

        Assert.AreEqual("Unsaved", dirty.Name);
        Assert.AreEqual(Visibility.Visible, dirty.UnsavedIndicatorVisibility);
        Assert.AreEqual(Visibility.Collapsed, clean.UnsavedIndicatorVisibility);
    }

    [TestMethod]
    public void AdvancedActionTile_CanRemainVisuallySelectedDuringMultiSelect()
    {
        var first = new AdvancedActionTile(new AutomationAction(), isRunning: false, removalPending: false, isSelected: true, isManagementLocked: false);
        var second = new AdvancedActionTile(new AutomationAction(), isRunning: false, removalPending: false, isSelected: true, isManagementLocked: false);

        Assert.IsTrue(first.IsSelected);
        Assert.IsTrue(second.IsSelected);
    }

    [TestMethod]
    public void AdvancedActionTile_HidesIndividualRemovalControlsDuringMultiSelect()
    {
        var tile = new AdvancedActionTile(new AutomationAction(), isRunning: false, removalPending: false, isSelected: true, isManagementLocked: false, isMultiSelection: true);

        Assert.AreEqual(Visibility.Collapsed, tile.RemoveButtonVisibility);
        Assert.AreEqual(Visibility.Collapsed, tile.RemovalConfirmationVisibility);
    }

    [TestMethod]
    public void AdvancedActionLabelConverter_CompactsMouseActionsBeforeKeyboardActions()
    {
        var converter = new AdvancedActionLabelConverter();
        var mouse = new AutomationAction { Settings = new AppDefaults { Input = "Right" } };
        var key = new AutomationAction { Settings = new AppDefaults { Input = "Space" } };

        Assert.AreEqual("Right click", converter.Convert([mouse, 120d], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("Right click", converter.Convert([mouse, 95d], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("R", converter.Convert([mouse, 75d], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("Right click", converter.Convert([mouse, 75d, false], typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("Space", converter.Convert([key, 95d], typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void AdvancedActionLabelConverter_ShowsPlaceholderForUnconfiguredActions()
    {
        var converter = new AdvancedActionLabelConverter();
        var unconfigured = new AutomationAction { Settings = new AppDefaults { Input = "Unset", MouseButton = "Unset" } };

        Assert.AreEqual("Set action", converter.Convert([unconfigured, 120d], typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void AdvancedActionLabels_ShowTheConfiguredActionType()
    {
        var action = new AutomationAction { Settings = new AppDefaults { Input = "Left", ClickType = "Double", Hotkey = 117 } };
        var converter = new AdvancedActionLabelConverter();

        Assert.AreEqual("Double left click", action.DisplayName.Split('·')[1].Trim());
        Assert.AreEqual("Double left click", converter.Convert([action, 120d], typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ProfileTransfer_RoundTripsOneSelfContainedProfile()
    {
        var path = TemporaryPath();
        try
        {
            var profile = new AutomationProfile
            {
                Id = "source",
                Name = "Games",
                BehaviorDefaults = new AppDefaults { RepeatUntilStopped = false, RepeatCount = 4, InputPulseMilliseconds = 3 },
                Actions = [new AutomationAction { Id = "f7", Settings = new AppDefaults { Hotkey = 118, Input = "Space", Milliseconds = 75 } }]
            };

            ProfileTransferStore.Save(path, profile);
            var imported = ProfileTransferStore.Load(path);

            Assert.AreEqual("Games", imported.Name);
            Assert.AreNotEqual(profile.Id, imported.Id);
            Assert.AreEqual(1, imported.Actions.Count);
            Assert.AreNotEqual(profile.Actions[0].Id, imported.Actions[0].Id);
            Assert.AreEqual(118, imported.Actions[0].Settings.Hotkey);
            Assert.AreEqual("Space", imported.Actions[0].Settings.Input);
            Assert.IsFalse(imported.BehaviorDefaults!.RepeatUntilStopped);
            Assert.AreEqual(3, imported.BehaviorDefaults.InputPulseMilliseconds);
        }
        finally { Delete(path); }
    }

    [TestMethod]
    public void ProfileCopy_SkipLeavesConflictsAndCopiesOtherHotkeys()
    {
        var destination = new AutomationProfile
        {
            Actions =
            [
                new AutomationAction { Id = "existing", Settings = new AppDefaults { Hotkey = 117, Input = "Right" } }
            ]
        };
        var conflicting = new AutomationAction { Id = "source-f6", Settings = new AppDefaults { Hotkey = 117, Input = "Space" } };
        var available = new AutomationAction { Id = "source-f7", Settings = new AppDefaults { Hotkey = 118, Input = "Enter" } };

        var result = AutomationProfileCopy.CopyTo(destination, [conflicting, available], ProfileCopyConflictResolution.Skip);

        Assert.AreEqual(1, result.CopiedCount);
        Assert.AreEqual(0, result.ReplacedCount);
        Assert.AreEqual(1, result.SkippedCount);
        Assert.AreEqual(2, destination.Actions.Count);
        Assert.AreEqual("Right", destination.Actions.Single(action => action.Settings.Hotkey == 117).Settings.Input);
        Assert.AreEqual("Enter", destination.Actions.Single(action => action.Settings.Hotkey == 118).Settings.Input);
    }

    [TestMethod]
    public void ProfileCopy_ReplaceReplacesConflictsWithNewIndependentActions()
    {
        var destination = new AutomationProfile
        {
            Actions = [new AutomationAction { Id = "existing", Settings = new AppDefaults { Hotkey = 117, Input = "Right" } }]
        };
        var source = new AutomationAction { Id = "source", Settings = new AppDefaults { Hotkey = 117, Input = "Space" } };

        var result = AutomationProfileCopy.CopyTo(destination, [source], ProfileCopyConflictResolution.Replace);

        Assert.AreEqual(1, result.CopiedCount);
        Assert.AreEqual(1, result.ReplacedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.AreEqual(1, destination.Actions.Count);
        Assert.AreEqual("Space", destination.Actions[0].Settings.Input);
        Assert.AreNotEqual(source.Id, destination.Actions[0].Id);
        Assert.AreNotSame(source.Settings, destination.Actions[0].Settings);
    }

    [TestMethod]
    public void ProfileCopy_PreservesUnselectedAction()
    {
        var source = new AutomationAction { Id = "source", Settings = new AppDefaults { Hotkey = 118, Input = "Unset", MouseButton = "Unset" } };
        var destination = new AutomationProfile();

        var result = AutomationProfileCopy.CopyTo(destination, [source], ProfileCopyConflictResolution.Skip);

        Assert.AreEqual(1, result.CopiedCount);
        Assert.AreEqual("Unset", destination.Actions[0].Settings.Input);
        Assert.AreEqual("Unset", destination.Actions[0].Settings.MouseButton);
        Assert.AreNotEqual(source.Id, destination.Actions[0].Id);
    }

    [TestMethod]
    public void ProfileCopy_NewProfileKeepsProfileDefaultsAndGivesCopiedActionsNewIds()
    {
        var source = new AutomationProfile
        {
            BehaviorDefaults = new AppDefaults { InputJitterMaximumMilliseconds = 7 },
            LightingDefaults = new RgbSettings { IndicatorColor = "#123456" },
            Actions = [new AutomationAction { Id = "f6", Settings = new AppDefaults { Hotkey = 117 } }]
        };

        var copy = AutomationProfileCopy.CreateNewProfile("Copied", source, source.Actions);

        Assert.AreEqual("Copied", copy.Name);
        Assert.AreEqual(7L, copy.BehaviorDefaults!.InputJitterMaximumMilliseconds);
        Assert.AreEqual("#123456", copy.LightingDefaults!.IndicatorColor);
        Assert.AreNotEqual(source.Actions[0].Id, copy.Actions[0].Id);
        copy.BehaviorDefaults.InputJitterMaximumMilliseconds = 2;
        copy.LightingDefaults.IndicatorColor = "#654321";
        Assert.AreEqual(7L, source.BehaviorDefaults.InputJitterMaximumMilliseconds);
        Assert.AreEqual("#123456", source.LightingDefaults.IndicatorColor);
    }

    [TestMethod]
    public void ProfileCopyDestination_UsesTheProfileNameAsItsDisplayText()
    {
        Assert.AreEqual("Games", new ProfileCopyDestination(new AutomationProfile { Name = "Games" }).ToString());
        Assert.AreEqual("New profile…", new ProfileCopyDestination(null).ToString());
    }

    [TestMethod]
    public void EmptyProfileDraft_IsDiscardableButProfileContentRequiresAChoice()
    {
        Assert.IsFalse(AutomationProfileDraftRules.HasContent(new AutomationProfile()));
        Assert.IsTrue(AutomationProfileDraftRules.HasContent(new AutomationProfile { Actions = [new AutomationAction()] }));
        Assert.IsTrue(AutomationProfileDraftRules.HasContent(new AutomationProfile { BehaviorDefaults = new AppDefaults { RepeatCount = 4 } }));
    }

    [TestMethod]
    public void ProfileActionOrder_MovesHotkeysAndIgnoresNoOpDrops()
    {
        var profile = new AutomationProfile
        {
            Actions =
            [
                new AutomationAction { Id = "f6" },
                new AutomationAction { Id = "f7" },
                new AutomationAction { Id = "f8" }
            ]
        };

        Assert.IsTrue(AutomationProfileActionOrder.Move(profile, "f8", "f6", placeAfter: false));
        CollectionAssert.AreEqual(new[] { "f8", "f6", "f7" }, profile.Actions.Select(action => action.Id).ToArray());
        Assert.IsTrue(AutomationProfileActionOrder.Move(profile, "f8", "f7", placeAfter: true));
        CollectionAssert.AreEqual(new[] { "f6", "f7", "f8" }, profile.Actions.Select(action => action.Id).ToArray());
        Assert.IsFalse(AutomationProfileActionOrder.Move(profile, "f6", "f7", placeAfter: false));
        CollectionAssert.AreEqual(new[] { "f6", "f7", "f8" }, profile.Actions.Select(action => action.Id).ToArray());
    }

    [TestMethod]
    public void ProfileHotkeyLimit_IsEnforcedForStoredAndCopiedActions()
    {
        var destination = new AutomationProfile
        {
            Actions = Enumerable.Range(1, AutomationProfileLimits.MaximumHotkeys)
                .Select(index => new AutomationAction { Id = $"existing-{index}", Settings = new AppDefaults { Hotkey = 100 + index } })
                .ToList()
        };
        var source = new AutomationAction { Id = "new", Settings = new AppDefaults { Hotkey = 200 } };

        var result = AutomationProfileCopy.CopyTo(destination, [source], ProfileCopyConflictResolution.Skip);

        Assert.AreEqual(AutomationProfileLimits.MaximumHotkeys, destination.Actions.Count);
        Assert.AreEqual(0, result.CopiedCount);
        Assert.AreEqual(1, result.SkippedCount);

        destination.Actions.Add(new AutomationAction { Id = "overflow" });
        Assert.AreEqual(1, AutomationProfileLimits.Enforce(destination));
        Assert.AreEqual(AutomationProfileLimits.MaximumHotkeys, destination.Actions.Count);
    }

    [TestMethod]
    public void AdvancedActionTile_HidesInlineControlsAtTheCompactThreshold()
    {
        var compact = new AdvancedActionTile(new AutomationAction(), false, false, false, false, showInlineActionControls: false);
        var normal = new AdvancedActionTile(new AutomationAction(), false, false, false, false);

        Assert.AreEqual(Visibility.Collapsed, compact.InlineActionControlsVisibility);
        Assert.AreEqual(3, compact.ActionLabelColumnSpan);
        Assert.AreEqual(Visibility.Visible, normal.InlineActionControlsVisibility);
        Assert.AreEqual(1, normal.ActionLabelColumnSpan);
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "AutoClicker.Tests", Guid.NewGuid().ToString("N"), "automation-profiles.json");
    private static void Delete(string path) { var directory = Path.GetDirectoryName(path)!; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
