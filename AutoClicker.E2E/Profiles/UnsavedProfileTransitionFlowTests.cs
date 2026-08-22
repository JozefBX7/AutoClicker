// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class UnsavedProfileTransitionFlowTests
{
    [DataTestMethod]
    [DataRow("Cancel")]
    [DataRow("Discard")]
    [DataRow("Save")]
    public void SwitchingProfiles_RespectsUnsavedChangesDecision(string decision)
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var options = new ProfileOptionsRobot(session);

        options.NewProfile();
        options.SaveDraftAs("Secondary profile");
        var secondaryId = fixture.ReadProfiles().Profiles.Single(profile => profile.Name == "Secondary profile").Id;

        session.MainElement($"Profile_{ProfileE2EFixture.ProfileId}").AsButton().Invoke();
        session.Editor.Select(EditorScope.Profile);
        session.Editor.EnterMilliseconds(876);

        session.MainElement($"Profile_{secondaryId}").AsButton().Invoke();
        var prompt = session.Dialog("Unsaved profile changes");
        prompt.FindFirstDescendant(condition => condition.ByAutomationId($"{decision}UnsavedProfileChanges"))!
            .AsButton().Invoke();

        if (decision == "Cancel")
        {
            session.WaitFor(() => session.Editor.ScopeHint.Contains("profile behavior defaults", StringComparison.Ordinal),
                "cancel did not leave the original profile selected");
            Assert.AreEqual(876, session.Editor.Milliseconds);
            Assert.AreEqual(ProfileE2EFixture.ProfileMilliseconds, StoredOriginalMilliseconds(fixture));
            return;
        }

        session.WaitFor(
            () => fixture.ReadProfiles().ActiveProfileId == secondaryId,
            $"{decision.ToLowerInvariant()} did not continue to the selected profile");
        Assert.AreEqual(
            decision == "Save" ? 876 : ProfileE2EFixture.ProfileMilliseconds,
            StoredOriginalMilliseconds(fixture));
    }

    private static int StoredOriginalMilliseconds(ProfileE2EFixture fixture) =>
        fixture.ReadProfiles().Profiles.Single(profile => profile.Id == ProfileE2EFixture.ProfileId)
            .BehaviorDefaults?.Milliseconds
        ?? throw new InvalidDataException("The seeded profile lost its behavior defaults.");
}
