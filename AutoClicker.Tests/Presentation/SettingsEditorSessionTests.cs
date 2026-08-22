// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SettingsEditorSessionTests
{
    [TestMethod]
    public void ScopeTransitions_KeepScopeAndSelectionAtomic()
    {
        var session = new SettingsEditorSession();

        Assert.AreEqual(SettingsEditorScopeKind.Simple, session.Scope.Kind);

        session.EnterSharedDefaults();
        Assert.AreEqual(SettingsEditorScopeKind.GlobalDefaults, session.Scope.Kind);
        Assert.AreEqual(0, session.SelectedActionCount);

        session.EnterProfileDefaults("profile");
        Assert.IsTrue(session.IsEditingProfile("profile"));
        Assert.AreEqual(0, session.SelectedActionCount);

        session.EnterHotkey("f6");
        Assert.IsTrue(session.IsEditingHotkey("f6"));
        CollectionAssert.AreEquivalent(new[] { "f6" }, session.SelectedActionIds.ToArray());

        session.EnterSimple();
        Assert.AreEqual(SettingsEditorScopeKind.Simple, session.Scope.Kind);
        Assert.AreEqual(0, session.SelectedActionCount);
    }

    [TestMethod]
    public void MultipleSelection_ShowsSharedValuesWithoutLosingHighlightedHotkeys()
    {
        var session = new SettingsEditorSession();
        session.EnterHotkey("f6");

        session.ToggleHotkey("f7");
        Assert.AreEqual(SettingsEditorScopeKind.MultipleHotkeys, session.Scope.Kind);
        CollectionAssert.AreEquivalent(new[] { "f6", "f7" }, session.SelectedActionIds.ToArray());

        session.EnterSharedDefaults(clearSelection: false);
        Assert.AreEqual(SettingsEditorScopeKind.MultipleHotkeys, session.Scope.Kind);
        CollectionAssert.AreEquivalent(new[] { "f6", "f7" }, session.SelectedActionIds.ToArray());

        session.ToggleHotkey("f6");
        Assert.IsTrue(session.IsEditingHotkey("f7"));
        CollectionAssert.AreEquivalent(new[] { "f7" }, session.SelectedActionIds.ToArray());
    }

    [TestMethod]
    public void SharedDefaults_CanRetainOneHighlightWithoutTreatingItAsAnEditTarget()
    {
        var session = new SettingsEditorSession();
        session.EnterHotkey("f6");

        session.EnterSharedDefaults(clearSelection: false);

        Assert.AreEqual(SettingsEditorScopeKind.GlobalDefaults, session.Scope.Kind);
        CollectionAssert.AreEquivalent(new[] { "f6" }, session.SelectedActionIds.ToArray());
        Assert.AreEqual(SettingsEditorStorageTarget.GlobalDefaults, session.StorageTarget(advancedMode: true, "profile", "f6"));
    }

    [TestMethod]
    public void RemovingHotkeys_RecomputesTheScopeFromTheRemainingSelection()
    {
        var session = new SettingsEditorSession();
        session.EnterHotkey("f6");
        session.ToggleHotkey("f7");

        session.RemoveHotkey("f6");
        Assert.IsTrue(session.IsEditingHotkey("f7"));

        session.RemoveHotkey("f7");
        Assert.AreEqual(SettingsEditorScopeKind.GlobalDefaults, session.Scope.Kind);
        Assert.AreEqual(0, session.SelectedActionCount);
    }

    [TestMethod]
    public void ProfileScope_CannotRetainAStaleHotkeySelection()
    {
        var session = new SettingsEditorSession();
        session.EnterHotkey("f6");

        session.EnterProfileDefaults("profile");

        Assert.IsTrue(session.IsEditingProfile("profile"));
        Assert.AreEqual(0, session.SelectedActionCount);
    }

    [TestMethod]
    public void DiscardTransition_DoesNotTreatAReloadedProfileWithTheSameIdAsStillBeingEdited()
    {
        var session = new SettingsEditorSession();
        session.EnterProfileDefaults("profile");

        session.EnterSharedDefaults(clearSelection: false);

        Assert.IsFalse(session.IsEditingProfile("profile"));
        Assert.AreEqual(SettingsEditorStorageTarget.GlobalDefaults, session.StorageTarget(advancedMode: true, "profile", activeActionId: null));
    }

    [TestMethod]
    public void DocumentReload_RestoresTheSelectedProfileEditorWhenTheProfileStillExists()
    {
        var previous = SettingsEditorScope.ProfileDefaults("profile");

        var restored = SettingsEditorPolicy.ResolveScopeAfterDocumentReload(previous, "profile", []);

        Assert.AreEqual(previous, restored);
    }

    [TestMethod]
    public void DocumentReload_RestoresTheSelectedHotkeyEditorWhenTheHotkeyStillExists()
    {
        var previous = SettingsEditorScope.Hotkey("f6");

        var restored = SettingsEditorPolicy.ResolveScopeAfterDocumentReload(previous, "profile", ["f6", "f7"]);

        Assert.AreEqual(previous, restored);
    }

    [TestMethod]
    public void DocumentReload_FallsBackToGlobalWhenThePreviousTargetNoLongerExists()
    {
        Assert.AreEqual(
            SettingsEditorScope.GlobalDefaults,
            SettingsEditorPolicy.ResolveScopeAfterDocumentReload(SettingsEditorScope.ProfileDefaults("old-profile"), "profile", []));
        Assert.AreEqual(
            SettingsEditorScope.GlobalDefaults,
            SettingsEditorPolicy.ResolveScopeAfterDocumentReload(SettingsEditorScope.Hotkey("old-action"), "profile", ["f6"]));
    }

    [TestMethod]
    public void StaleScopeTarget_CannotWriteIntoANewActiveProfileOrHotkey()
    {
        var session = new SettingsEditorSession();

        session.EnterProfileDefaults("old-profile");
        Assert.AreEqual(SettingsEditorStorageTarget.GlobalDefaults, session.StorageTarget(advancedMode: true, "new-profile", activeActionId: null));

        session.EnterHotkey("old-action");
        Assert.AreEqual(SettingsEditorStorageTarget.GlobalDefaults, session.StorageTarget(advancedMode: true, "new-profile", "new-action"));
        Assert.AreEqual(SettingsEditorStorageTarget.HotkeyOverride, session.StorageTarget(advancedMode: true, "new-profile", "old-action"));
        Assert.AreEqual(SettingsEditorStorageTarget.SimpleDefaults, session.StorageTarget(advancedMode: false, "new-profile", "old-action"));

        session.EnterSimple();
        Assert.AreEqual(SettingsEditorStorageTarget.GlobalDefaults, session.StorageTarget(advancedMode: true, "new-profile", "new-action"));
    }

    [TestMethod]
    public void DirtyState_UsesDraftIdentityAndTheSavedFingerprint()
    {
        var profile = new AutomationProfile { Id = "profile", Name = "Saved" };
        var document = new AutomationProfileDocument { ActiveProfileId = profile.Id, Profiles = [profile] };
        var saved = AutomationProfileConfiguration.Fingerprint(document);

        Assert.IsFalse(SettingsEditorDirtyState.IsProfileDocumentDirty(document, saved, profile.Id, unsavedProfileId: null));

        var emptyDocument = new AutomationProfileDocument();
        Assert.IsFalse(SettingsEditorDirtyState.IsProfileDocumentDirty(
            emptyDocument,
            AutomationProfileConfiguration.Fingerprint(emptyDocument),
            activeProfileId: null,
            unsavedProfileId: null));
        Assert.IsTrue(SettingsEditorDirtyState.IsProfileDocumentDirty(document, saved, profile.Id, unsavedProfileId: profile.Id));

        profile.Name = "Changed";
        Assert.IsTrue(SettingsEditorDirtyState.IsProfileDocumentDirty(document, saved, profile.Id, unsavedProfileId: null));
        profile.Name = "Saved";
        Assert.IsFalse(SettingsEditorDirtyState.IsProfileDocumentDirty(document, saved, profile.Id, unsavedProfileId: null));
    }

    [DataTestMethod]
    [DataRow((int)SettingsEditorScopeKind.Simple, (int)SettingsEditorStorageTarget.SimpleDefaults)]
    [DataRow((int)SettingsEditorScopeKind.GlobalDefaults, (int)SettingsEditorStorageTarget.GlobalDefaults)]
    [DataRow((int)SettingsEditorScopeKind.ProfileDefaults, (int)SettingsEditorStorageTarget.ProfileDefaults)]
    [DataRow((int)SettingsEditorScopeKind.Hotkey, (int)SettingsEditorStorageTarget.HotkeyOverride)]
    [DataRow((int)SettingsEditorScopeKind.MultipleHotkeys, (int)SettingsEditorStorageTarget.GlobalDefaults)]
    public void StorageTarget_IsDefinedOnceForEveryEditorScope(int scope, int expected)
    {
        Assert.AreEqual((SettingsEditorStorageTarget)expected, SettingsEditorPolicy.StorageTarget((SettingsEditorScopeKind)scope));
    }

    [DataTestMethod]
    [DataRow((int)SettingsEditorTextFieldKind.Interval)]
    [DataRow((int)SettingsEditorTextFieldKind.RepeatCount)]
    [DataRow((int)SettingsEditorTextFieldKind.CursorPosition)]
    [DataRow((int)SettingsEditorTextFieldKind.TargetWindow)]
    public void Enter_SubmitsEverySupportedEditorTextField(int field)
    {
        Assert.IsTrue(SettingsEditorPolicy.ShouldSubmitTextField(
            (SettingsEditorTextFieldKind)field,
            enterPressed: true,
            inputCapturePending: false));
    }

    [TestMethod]
    public void EditorTextSubmission_DoesNotStealOtherKeysOrInputCapture()
    {
        Assert.IsFalse(SettingsEditorPolicy.ShouldSubmitTextField(
            SettingsEditorTextFieldKind.Interval,
            enterPressed: false,
            inputCapturePending: false));
        Assert.IsFalse(SettingsEditorPolicy.ShouldSubmitTextField(
            SettingsEditorTextFieldKind.Interval,
            enterPressed: true,
            inputCapturePending: true));
        Assert.IsFalse(SettingsEditorPolicy.ShouldSubmitTextField(
            SettingsEditorTextFieldKind.None,
            enterPressed: true,
            inputCapturePending: false));
    }
}
