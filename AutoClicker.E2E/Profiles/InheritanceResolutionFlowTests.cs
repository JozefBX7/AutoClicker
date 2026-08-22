// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class InheritanceResolutionFlowTests
{
    [TestMethod]
    public void IntervalHierarchy_ResolvesGlobalProfileAndHotkeyValuesAfterRevertOverrideAndRestart()
    {
        using var fixture = new ProfileE2EFixture();
        using (var session = fixture.Launch())
        {
            var actions = new AdvancedActionRobot(session);
            var profiles = new ProfileOptionsRobot(session);

            actions.ChooseMenu(ProfileE2EFixture.ActionId, "ToggleActionSharedBehavior");
            profiles.RevertOnlyInterval(profiles.SharedDefaultsDialog());
            profiles.SaveExisting();
            session.Editor.Select(EditorScope.Hotkey);
            Assert.AreEqual(ProfileE2EFixture.ProfileMilliseconds, session.Editor.Milliseconds,
                "the hotkey did not inherit its profile interval");

            profiles.ChooseMenu(ProfileE2EFixture.ProfileId, "ChooseProfileInheritedSections");
            profiles.RevertOnlyInterval(profiles.SharedDefaultsDialog());
            profiles.SaveExisting();
            session.Editor.Select(EditorScope.Profile);
            Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds, session.Editor.Milliseconds);
            session.Editor.Select(EditorScope.Hotkey);
            Assert.AreEqual(ProfileE2EFixture.GlobalMilliseconds, session.Editor.Milliseconds,
                "the fully inherited hotkey did not resolve the global interval");

            session.Editor.Select(EditorScope.Global);
            session.Editor.EnterMilliseconds(611);
            session.Editor.Select(EditorScope.Profile);
            Assert.AreEqual(611, session.Editor.Milliseconds);
            session.Editor.Select(EditorScope.Hotkey);
            Assert.AreEqual(611, session.Editor.Milliseconds);

            session.Editor.Select(EditorScope.Profile);
            session.Editor.OverrideInterval();
            session.Editor.EnterMilliseconds(622);
            session.Editor.SaveProfile();
            session.Editor.Select(EditorScope.Hotkey);
            Assert.AreEqual(622, session.Editor.Milliseconds,
                "the inherited hotkey did not follow the new profile override");

            session.Editor.OverrideInterval();
            session.Editor.EnterMilliseconds(633);
            session.Editor.SaveProfile();
            session.Editor.Select(EditorScope.Global);
            Assert.AreEqual(611, session.Editor.Milliseconds);
            session.Editor.Select(EditorScope.Profile);
            Assert.AreEqual(622, session.Editor.Milliseconds);
            session.Editor.Select(EditorScope.Hotkey);
            Assert.AreEqual(633, session.Editor.Milliseconds);
        }

        using var restarted = fixture.Launch();
        restarted.Editor.Select(EditorScope.Global);
        Assert.AreEqual(611, restarted.Editor.Milliseconds);
        restarted.Editor.Select(EditorScope.Profile);
        Assert.AreEqual(622, restarted.Editor.Milliseconds);
        restarted.Editor.Select(EditorScope.Hotkey);
        Assert.AreEqual(633, restarted.Editor.Milliseconds);

        var storedProfile = fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId);
        Assert.IsTrue(storedProfile.ActiveBehaviorOverrides.HasFlag(AutomationBehaviorOverride.Interval));
        Assert.IsTrue(storedProfile.Actions.Single(action => action.Id == ProfileE2EFixture.ActionId)
            .ActiveBehaviorOverrides.HasFlag(AutomationBehaviorOverride.Interval));
    }
}
