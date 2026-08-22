// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class BackupScopeInfoTests
{
    [DataTestMethod]
    [DataRow(BackupScope.Everything, "Everything", "AutoClicker-backup.autoclicker-backup.json")]
    [DataRow(BackupScope.SimpleMode, "Simple mode settings", "AutoClicker-simple-settings.autoclicker-simple.json")]
    [DataRow(BackupScope.AdvancedMode, "Advanced mode settings and profiles", "AutoClicker-advanced-settings.autoclicker-advanced.json")]
    [DataRow(BackupScope.CustomSequences, "Custom sequences", "AutoClicker-custom-sequences.autoclicker-sequences.json")]
    public void ScopeMetadata_UsesStableUserFacingNamesAndFileNames(BackupScope scope, string displayName, string defaultFileName)
    {
        Assert.AreEqual(displayName, BackupScopeInfo.DisplayName(scope));
        Assert.AreEqual(defaultFileName, BackupScopeInfo.DefaultFileName(scope));
        StringAssert.Contains(BackupScopeInfo.ExportFilter(scope), defaultFileName[(defaultFileName.IndexOf('.') + 1)..]);
    }

    [DataTestMethod]
    [DataRow(BackupScope.SimpleMode)]
    [DataRow(BackupScope.AdvancedMode)]
    [DataRow(BackupScope.CustomSequences)]
    public void FocusedImportFilter_AcceptsItsOwnAndCompleteBackupFormats(BackupScope scope)
    {
        var filter = BackupScopeInfo.ImportFilter(scope);

        StringAssert.Contains(filter, BackupScopeInfo.FileExtension(scope));
        StringAssert.Contains(filter, BackupScopeInfo.FileExtension(BackupScope.Everything));
    }
}
