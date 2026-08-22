// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class ProfileOptionsFlowTests
{
    [TestMethod]
    public void NewRenameDuplicateAndDeleteProfile_PersistExpectedDocumentsAndFreshIds()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var profiles = new ProfileOptionsRobot(session);

        profiles.NewProfile();
        profiles.SaveDraftAs("Created profile");
        var created = fixture.ReadProfiles().Profiles.Single(profile => profile.Name == "Created profile");

        profiles.Rename(created.Id, "Renamed profile");
        profiles.SaveExisting();
        session.WaitFor(() => fixture.ReadProfiles().Profiles.Any(profile => profile.Name == "Renamed profile"),
            "renamed profile was not persisted");

        profiles.ChooseMenu(created.Id, "DuplicateProfile");
        profiles.SaveDraftAs("Copied profile");
        var document = fixture.ReadProfiles();
        var renamed = document.Profiles.Single(profile => profile.Name == "Renamed profile");
        var copied = document.Profiles.Single(profile => profile.Name == "Copied profile");
        Assert.AreNotEqual(renamed.Id, copied.Id);
        CollectionAssert.AreEqual(
            renamed.Actions.Select(action => action.DisplayName).ToList(),
            copied.Actions.Select(action => action.DisplayName).ToList());
        Assert.IsFalse(renamed.Actions.Select(action => action.Id).Intersect(copied.Actions.Select(action => action.Id)).Any());

        profiles.ChooseMenu(created.Id, "DeleteProfile");
        profiles.Confirm();
        session.WaitFor(() => fixture.ReadProfiles().Profiles.All(profile => profile.Id != created.Id),
            "deleted profile remained in storage");
        Assert.AreEqual(2, fixture.ReadProfiles().Profiles.Count);
    }

    [TestMethod]
    public void ProfileBehaviorInheritance_CanRevertOneSectionThenAllSections()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var profiles = new ProfileOptionsRobot(session);

        profiles.ChooseMenu(ProfileE2EFixture.ProfileId, "ChooseProfileInheritedSections");
        profiles.RevertOnlyInterval(profiles.SharedDefaultsDialog());
        profiles.SaveExisting();
        var partiallyInherited = fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
        Assert.IsFalse(partiallyInherited.BehaviorOverrides.HasFlag(AutomationBehaviorOverride.Interval));
        Assert.IsTrue(partiallyInherited.BehaviorOverrides.HasFlag(AutomationBehaviorOverride.Repeat));

        profiles.ChooseMenu(ProfileE2EFixture.ProfileId, "UseAppDefaultsForProfile");
        profiles.SaveExisting();
        var inherited = fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
        Assert.AreEqual(AutomationBehaviorOverride.None, inherited.ActiveBehaviorOverrides);
    }

    [TestMethod]
    public void ProfileLightingOverride_CanBeSavedAndReturnedToAppDefaults()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var profiles = new ProfileOptionsRobot(session);

        profiles.ConfigureProfileLighting(ProfileE2EFixture.ProfileId, enabled: true, effect: "Blink");
        profiles.SaveExisting();
        var overridden = fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
        Assert.IsNotNull(overridden.LightingDefaults);
        Assert.IsTrue(overridden.LightingDefaults.Enabled);
        Assert.AreEqual("Blink", overridden.LightingDefaults.LightingEffect);

        profiles.ChooseMenu(ProfileE2EFixture.ProfileId, "UseAppLightingForProfile");
        profiles.SaveExisting();
        Assert.IsNull(fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId).LightingDefaults);
    }
}
