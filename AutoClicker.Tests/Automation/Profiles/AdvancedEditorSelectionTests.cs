using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AdvancedEditorSelectionTests
{
    [TestMethod]
    public void SharedDefaultsSelection_StaysInPlaceOutsideTheFooter()
    {
        Assert.IsFalse(MainWindow.ShouldReturnToSharedDefaults(advancedMode: true, isWithinActionTile: false, isWithinFooter: false));
        Assert.IsFalse(MainWindow.ShouldReturnToSharedDefaults(advancedMode: true, isWithinActionTile: true, isWithinFooter: true));
    }

    [TestMethod]
    public void SharedDefaultsSelection_ReturnsWhenClickingEmptyFooterSpaceEvenWithoutAnActionSelected()
    {
        Assert.IsTrue(MainWindow.ShouldReturnToSharedDefaults(advancedMode: true, isWithinActionTile: false, isWithinFooter: true));
        Assert.IsFalse(MainWindow.ShouldReturnToSharedDefaults(advancedMode: false, isWithinActionTile: false, isWithinFooter: true));
    }

    [TestMethod]
    public void SharedDefaultsSelection_ReturnsFromNonInteractiveEditorSpace()
    {
        Assert.IsTrue(MainWindow.ShouldReturnFromEditorDeadSpace(advancedMode: true, isEditorDeadSpace: true));
        Assert.IsFalse(MainWindow.ShouldReturnFromEditorDeadSpace(advancedMode: true, isEditorDeadSpace: false));
        Assert.IsFalse(MainWindow.ShouldReturnFromEditorDeadSpace(advancedMode: false, isEditorDeadSpace: true));
    }

    [TestMethod]
    public void PendingInterval_CommitsAndReleasesFocusBeforeABackdropChangesTheEditorScope()
    {
        Assert.IsTrue(SettingsEditorPolicy.ShouldCommitAndReleasePendingIntervalBeforeTransition(intervalHasKeyboardFocus: true, editorTransition: true));
        Assert.IsFalse(SettingsEditorPolicy.ShouldCommitAndReleasePendingIntervalBeforeTransition(intervalHasKeyboardFocus: false, editorTransition: true));
    }

    [TestMethod]
    public void PendingInterval_WaitsForNormalFocusLossWhenTheEditorScopeStaysPut()
    {
        Assert.IsFalse(SettingsEditorPolicy.ShouldCommitAndReleasePendingIntervalBeforeTransition(intervalHasKeyboardFocus: true, editorTransition: false));
    }
}
