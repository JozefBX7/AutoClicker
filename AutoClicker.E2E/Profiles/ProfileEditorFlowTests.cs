// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class ProfileEditorFlowTests
{
    [DataTestMethod]
    [DataRow((int)EditorScope.Global, 410)]
    [DataRow((int)EditorScope.Profile, 420)]
    [DataRow((int)EditorScope.Hotkey, 430)]
    public void Enter_CommitsIntervalOnlyToTheSelectedScopeAndPersistsAcrossRestart(int scopeValue, int milliseconds)
    {
        var scope = (EditorScope)scopeValue;
        using var fixture = new ProfileE2EFixture();
        using (var session = fixture.Launch())
        {
            session.Editor.Select(scope);
            session.Editor.EnterMilliseconds(milliseconds);
            Assert.IsFalse(session.Editor.MillisecondsHasKeyboardFocus);
            if (scope != EditorScope.Global) session.Editor.SaveProfile();
        }

        AssertStoredInterval(fixture, scope, milliseconds);

        using var restarted = fixture.Launch();
        restarted.Editor.Select(scope);
        Assert.AreEqual(milliseconds, restarted.Editor.Milliseconds);
    }

    [DataTestMethod]
    [DataRow((int)EditorScope.Global, 510)]
    [DataRow((int)EditorScope.Profile, 520)]
    [DataRow((int)EditorScope.Hotkey, 530)]
    public void Backdrop_CommitsPendingIntervalToTheScopeBeingLeft(int scopeValue, int milliseconds)
    {
        var scope = (EditorScope)scopeValue;
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        session.Editor.Select(scope);
        session.Editor.TypeMillisecondsWithoutSubmitting(milliseconds);

        session.Editor.ClickGlobalBackdrop();

        Assert.IsFalse(session.Editor.MillisecondsHasKeyboardFocus);
        if (scope != EditorScope.Global)
        {
            session.Editor.Select(scope);
            Assert.AreEqual(milliseconds, session.Editor.Milliseconds);
            session.Editor.SaveProfile();
        }
        AssertStoredInterval(fixture, scope, milliseconds);
    }

    [TestMethod]
    public void Discard_RestoresSavedProfileIntervalAndKeepsProfileEditorSelected()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        session.Editor.Select(EditorScope.Profile);
        session.Editor.EnterMilliseconds(999);

        session.Editor.DiscardSelectedProfileChanges();

        Assert.AreEqual(ProfileE2EFixture.ProfileMilliseconds, session.Editor.Milliseconds);
        StringAssert.Contains(session.Editor.ScopeHint, "profile behavior defaults");
        AssertStoredInterval(fixture, EditorScope.Profile, ProfileE2EFixture.ProfileMilliseconds);
    }

    [TestMethod]
    public void GlobalProfileAndHotkeyDefaults_SaveAtTheRightBoundariesAndPersistTogether()
    {
        ScopeDefaultsScenario[] scenarios =
        [
            new(EditorScope.Global, 610, 41, 101, 102, "changed-global.exe"),
            new(EditorScope.Profile, 620, 42, 201, 202, "changed-profile.exe"),
            new(EditorScope.Hotkey, 630, 43, 301, 302, "changed-hotkey.exe")
        ];

        using var fixture = new ProfileE2EFixture();
        using (var session = fixture.Launch())
        {
            foreach (var scenario in scenarios)
            {
                var storedMillisecondsBeforeEdit = ReadStoredSettings(fixture, scenario.Scope).Milliseconds;

                EnterSettings(session.Editor, scenario);

                if (scenario.Scope != EditorScope.Global)
                {
                    Assert.AreEqual(storedMillisecondsBeforeEdit, ReadStoredSettings(fixture, scenario.Scope).Milliseconds,
                        $"unsaved {scenario.Scope} defaults were written before Save profile");
                    session.Editor.SaveProfile();
                }

                AssertSettings(ReadStoredSettings(fixture, scenario.Scope), scenario);
            }
        }

        using var restarted = fixture.Launch();
        foreach (var scenario in scenarios)
        {
            restarted.Editor.Select(scenario.Scope);
            Assert.AreEqual(scenario.Milliseconds, restarted.Editor.Milliseconds);
            Assert.AreEqual(scenario.RepeatCount, restarted.Editor.RepeatCount);
            Assert.AreEqual(scenario.X, restarted.Editor.CursorX);
            Assert.AreEqual(scenario.Y, restarted.Editor.CursorY);
            Assert.AreEqual(scenario.Executable, restarted.Editor.TargetExecutable);
            Assert.IsTrue(restarted.Editor.TargetWindowEnabled);
        }
    }

    private static void AssertStoredInterval(ProfileE2EFixture fixture, EditorScope changedScope, int expected)
    {
        var global = fixture.ReadGlobalDefaults();
        var profile = fixture.ReadProfiles().Profiles.Single(item => item.Id == ProfileE2EFixture.ProfileId);
        var action = profile.Actions.Single(item => item.Id == ProfileE2EFixture.ActionId);

        Assert.AreEqual(changedScope == EditorScope.Global ? expected : ProfileE2EFixture.GlobalMilliseconds, global.Milliseconds);
        Assert.AreEqual(changedScope == EditorScope.Profile ? expected : ProfileE2EFixture.ProfileMilliseconds, profile.BehaviorDefaults!.Milliseconds);
        Assert.AreEqual(changedScope == EditorScope.Hotkey ? expected : ProfileE2EFixture.HotkeyMilliseconds, action.Settings.Milliseconds);
    }

    private static void EnterSettings(ProfileEditorRobot editor, ScopeDefaultsScenario scenario)
    {
        editor.Select(scenario.Scope);
        editor.EnterMilliseconds(scenario.Milliseconds);
        editor.EnterRepeatCount(scenario.RepeatCount);
        editor.EnterCursorPosition(scenario.X, scenario.Y);
        editor.EnterTargetExecutable(scenario.Executable);
    }

    private static AppDefaults ReadStoredSettings(ProfileE2EFixture fixture, EditorScope scope)
    {
        if (scope == EditorScope.Global) return fixture.ReadGlobalDefaults();

        var profile = fixture.ReadProfiles().Profiles.Single(item => item.Id == ProfileE2EFixture.ProfileId);
        return scope == EditorScope.Profile
            ? profile.BehaviorDefaults!
            : profile.Actions.Single(item => item.Id == ProfileE2EFixture.ActionId).Settings;
    }

    private static void AssertSettings(AppDefaults settings, ScopeDefaultsScenario expected)
    {
        Assert.AreEqual(expected.Milliseconds, settings.Milliseconds);
        Assert.AreEqual(expected.RepeatCount, settings.RepeatCount);
        Assert.AreEqual(expected.X, settings.X);
        Assert.AreEqual(expected.Y, settings.Y);
        Assert.AreEqual(expected.Executable, settings.TargetExecutable);
        Assert.IsTrue(settings.TargetWindowEnabled);
    }

    private readonly record struct ScopeDefaultsScenario(
        EditorScope Scope,
        int Milliseconds,
        int RepeatCount,
        int X,
        int Y,
        string Executable);
}
