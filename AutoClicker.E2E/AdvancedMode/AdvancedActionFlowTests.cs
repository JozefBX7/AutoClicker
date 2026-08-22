// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class AdvancedActionFlowTests
{
    [TestMethod]
    public void AddCancelChangeAndDisableHotkeys_PersistOnlyCompletedActions()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var actions = new AdvancedActionRobot(session);
        var profiles = new ProfileOptionsRobot(session);

        actions.CancelAddingHotkey();
        Assert.AreEqual(2, ActiveProfile(fixture).Actions.Count);

        actions.AddHotkey(VirtualKeyShort.F9);
        profiles.SaveExisting();
        var added = ActiveProfile(fixture).Actions.Single(action => action.Settings.Hotkey == 120);
        Assert.AreEqual(3, ActiveProfile(fixture).Actions.Count);

        actions.ChangeHotkey(added.Id, VirtualKeyShort.F10);
        profiles.SaveExisting();
        Assert.AreEqual(121, ActiveProfile(fixture).Actions.Single(action => action.Id == added.Id).Settings.Hotkey);

        actions.ChooseMenu(added.Id, "ToggleActionEnabled");
        profiles.SaveExisting();
        Assert.IsFalse(ActiveProfile(fixture).Actions.Single(action => action.Id == added.Id).HotkeyEnabled);
    }

    [TestMethod]
    public void HotkeyBehaviorAndLightingOverrides_CanBeConfiguredAndReverted()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var actions = new AdvancedActionRobot(session);
        var profiles = new ProfileOptionsRobot(session);

        actions.ConfigureLighting(ProfileE2EFixture.ActionId, enabled: true, effect: "Pulse");
        profiles.SaveExisting();
        var lit = ActiveProfile(fixture).Actions.Single(action => action.Id == ProfileE2EFixture.ActionId);
        Assert.IsFalse(lit.UsesSharedLightingSettings);
        Assert.IsTrue(lit.LightingOverride?.Enabled);
        Assert.AreEqual("Fade", lit.LightingOverride?.LightingEffect);

        actions.RevertAllBehavior(ProfileE2EFixture.ActionId);
        profiles.SaveExisting();
        Assert.AreEqual(
            AutomationBehaviorOverride.None,
            ActiveProfile(fixture).Actions.Single(action => action.Id == ProfileE2EFixture.ActionId).ActiveBehaviorOverrides);

        actions.ChooseMenu(ProfileE2EFixture.ActionId, "ToggleActionSharedLighting");
        profiles.SaveExisting();
        var inheritedLighting = ActiveProfile(fixture).Actions.Single(action => action.Id == ProfileE2EFixture.ActionId);
        Assert.IsTrue(inheritedLighting.UsesSharedLightingSettings);
        Assert.IsNull(inheritedLighting.LightingOverride);
    }

    [TestMethod]
    public void CopyHotkeyToNewProfile_CreatesIndependentActionIdsAndPersists()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var actions = new AdvancedActionRobot(session);
        var profiles = new ProfileOptionsRobot(session);

        actions.CopyToNewProfile(ProfileE2EFixture.ActionId, "Copied hotkey profile");
        profiles.SaveExisting();

        var document = fixture.ReadProfiles();
        var destination = document.Profiles.Single(profile => profile.Name == "Copied hotkey profile");
        Assert.AreEqual(1, destination.Actions.Count);
        Assert.AreNotEqual(ProfileE2EFixture.ActionId, destination.Actions[0].Id);
        Assert.AreEqual(117, destination.Actions[0].Settings.Hotkey);
    }

    [TestMethod]
    public void MultiSelection_ContextOptionsApplyToEverySelectedHotkey_AndCanDeleteThemTogether()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var actions = new AdvancedActionRobot(session);
        var profiles = new ProfileOptionsRobot(session);

        actions.SelectTogether(ProfileE2EFixture.ActionId, ProfileE2EFixture.SecondActionId);
        var app = new MainWindowRobot(session);
        session.WaitFor(() => app.AdvancedStatus.Contains("2 hotkeys selected", StringComparison.OrdinalIgnoreCase),
            "multi-selection was not reflected by the editor");
        actions.ChooseMenu(ProfileE2EFixture.SecondActionId, "ToggleActionEnabled");
        profiles.SaveExisting();
        Assert.IsTrue(ActiveProfile(fixture).Actions.All(action => !action.HotkeyEnabled));

        actions.SelectTogether(ProfileE2EFixture.ActionId, ProfileE2EFixture.SecondActionId);
        actions.ChooseMenu(ProfileE2EFixture.SecondActionId, "DeleteSelectedActions");
        profiles.Confirm();
        profiles.SaveExisting();
        session.WaitFor(() => ActiveProfile(fixture).Actions.Count == 0,
            "selected hotkeys were not deleted together");
    }

    private static AutomationProfile ActiveProfile(ProfileE2EFixture fixture) =>
        fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
}
