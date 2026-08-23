// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class ConfigurationStorageTests
{
    private const string TestDirectoryName = "AutoClicker.Tests";
    [TestMethod]
    public void SequenceLibrary_RoundTripsNamedPresetsAndSteps()
    {
        var path = TemporaryPath(ConfigurationFileNames.SequenceLibrary);
        try
        {
            SequenceLibraryStore.Save(path, [new SequencePreset { Name = "Combat", UseGlobalInputPulse = false, Steps = [new SequenceStep { Input = AutomationInputIds.Left }, new SequenceStep { Input = AutomationInputIds.Space, DelayAfterMilliseconds = 80 }] }]);
            var library = SequenceLibraryStore.Load(path);
            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("Combat", library[0].Name);
            Assert.AreEqual(2, library[0].Steps.Count);
            Assert.IsFalse(library[0].UseGlobalInputPulse);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void SequenceLibrary_RoundTripsBalancedHoldAndReleaseEvents()
    {
        var path = TemporaryPath("sequence-library-holds.json");
        try
        {
            SequenceLibraryStore.Save(path,
            [
                new SequencePreset
                {
                    Name = "Held chord",
                    Steps =
                    [
                        new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x11, Mode = SequenceStepMode.Hold },
                        new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x43 },
                        new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 0x11, Mode = SequenceStepMode.Release }
                    ]
                }
            ]);

            var steps = SequenceLibraryStore.Load(path).Single().Steps;
            CollectionAssert.AreEqual(
                new[] { SequenceStepMode.Hold, SequenceStepMode.Press, SequenceStepMode.Release },
                steps.Select(step => step.Mode).ToArray());
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void SequenceLibrary_IgnoresUnbalancedStatefulPreset()
    {
        const string json = """
            {"SchemaVersion":2,"Presets":[
              {"Id":"unsafe","Name":"Unsafe","Steps":[{"Input":"Left","Mode":1},{"Input":"Delay","DelayAfterMilliseconds":100}]},
              {"Id":"safe","Name":"Safe","Steps":[{"Input":"Left"},{"Input":"Right"}]}
            ]}
            """;

        var library = SequenceLibraryStore.Deserialize(json);

        Assert.AreEqual(1, library.Count);
        Assert.AreEqual("safe", library[0].Id);
    }

    [TestMethod]
    public void SequenceLibrary_IgnoresMalformedAndDuplicatePresetsWithoutDiscardingValidPresets()
    {
        const string json = """
            {"SchemaVersion":1,"Presets":[
              {"Id":"valid","Name":"Valid","Steps":[{"Input":"Left"},{"Input":"Right"}]},
              {"Id":"invalid-action","Name":"Invalid action","Steps":[{"Input":"Left"},{"Input":"Unknown"}]},
              {"Id":"valid","Name":"Duplicate","Steps":[{"Input":"Space"},{"Input":"Enter"}]},
              {"Id":"too-short","Name":"Too short","Steps":[{"Input":"Left"}]}
            ]}
            """;

        var library = SequenceLibraryStore.Deserialize(json);

        Assert.AreEqual(1, library.Count);
        Assert.AreEqual("valid", library[0].Id);
        Assert.AreEqual("Valid", library[0].Name);
    }

    [TestMethod]
    public void SequenceLibrary_RejectsFutureSchemas()
    {
        var json = "{" + "\"SchemaVersion\":" + (SequenceLibraryStore.CurrentSchemaVersion + 1) + ",\"Presets\":[]}";

        Assert.ThrowsException<InvalidDataException>(() => SequenceLibraryStore.Deserialize(json));
    }

    [TestMethod]
    public void FullBackup_PreservesSequenceLibrary()
    {
        var path = TemporaryPath("backup-with-sequences.json");
        try
        {
            const string sequenceLibraryJson = """
                {"SchemaVersion":1,"Presets":[{"Id":"work","Name":"Work","Steps":[{"Input":"Space"},{"Input":"Delay","DelayAfterMilliseconds":250}]}]}
                """;
            ConfigBackupStore.Write(path, new ConfigBackupDocument { LegacySharedDefaultsJson = "{}", SequenceLibraryJson = sequenceLibraryJson });

            var backup = ConfigBackupStore.Read(path);
            var library = SequenceLibraryStore.Deserialize(backup.SequenceLibraryJson);

            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("Work", library[0].Name);
            Assert.AreEqual(250, library[0].Steps[1].DelayAfterMilliseconds);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void SequenceLibrary_LoadsLegacyDocumentWithoutSchemaVersion()
    {
        const string json = """
            {"Presets":[{"Id":"legacy-combat","Name":"Combat","Steps":[{"Input":"Left"},{"Input":"Custom","CustomKey":65,"DelayAfterMilliseconds":80},{"Input":"Delay","DelayAfterMilliseconds":125}]}]}
            """;

        var library = SequenceLibraryStore.Deserialize(json);

        Assert.AreEqual(1, library.Count);
        Assert.AreEqual("legacy-combat", library[0].Id);
        Assert.AreEqual("Combat", library[0].Name);
        CollectionAssert.AreEqual(new[] { "Left", "Custom", "Delay" }, library[0].Steps.Select(step => step.Input).ToArray());
        Assert.AreEqual(65, library[0].Steps[1].CustomKey);
        Assert.AreEqual(125, library[0].Steps[2].DelayAfterMilliseconds);
        Assert.IsTrue(library[0].UseGlobalInputPulse);
    }

    [TestMethod]
    public void SequenceLibrary_RoundTripsAllSupportedStepTypes()
    {
        var path = TemporaryPath("all-sequence-steps.json");
        var steps = new[]
        {
            new SequenceStep { Input = AutomationInputIds.Left, DelayAfterMilliseconds = 10 },
            new SequenceStep { Input = AutomationInputIds.Right, DelayAfterMilliseconds = 20 },
            new SequenceStep { Input = AutomationInputIds.Middle, DelayAfterMilliseconds = 30 },
            new SequenceStep { Input = AutomationInputIds.Space, DelayAfterMilliseconds = 40 },
            new SequenceStep { Input = AutomationInputIds.Enter, DelayAfterMilliseconds = 50 },
            new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 65, DelayAfterMilliseconds = 60 },
            new SequenceStep { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = 70 }
        };
        try
        {
            SequenceLibraryStore.Save(path, [new SequencePreset { Id = "all-actions", Name = "All actions", Steps = steps.ToList() }]);
            var library = SequenceLibraryStore.Load(path);

            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("all-actions", library[0].Id);
            CollectionAssert.AreEqual(steps.Select(step => step.Input).ToArray(), library[0].Steps.Select(step => step.Input).ToArray());
            CollectionAssert.AreEqual(steps.Select(step => step.DelayAfterMilliseconds).ToArray(), library[0].Steps.Select(step => step.DelayAfterMilliseconds).ToArray());
            Assert.AreEqual(65, library[0].Steps[5].CustomKey);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void FullBackup_RoundTripsVersionedDocument()
    {
        var path = TemporaryPath("backup.json");
        try
        {
            ConfigBackupStore.Write(path, new ConfigBackupDocument { LegacySharedDefaultsJson = "{}", SequenceLibraryJson = "{\"SchemaVersion\":1,\"Presets\":[]}" });
            var backup = ConfigBackupStore.Read(path);
            Assert.AreEqual(ConfigBackupStore.CurrentSchemaVersion, backup.SchemaVersion);
            Assert.AreEqual("{}", backup.LegacySharedDefaultsJson);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void FullBackup_RoundTripsRgbIdleProfileName()
    {
        var path = TemporaryPath("backup-rgb.json");
        try
        {
            ConfigBackupStore.Write(path, new ConfigBackupDocument
            {
                Scope = BackupScope.Everything,
                LegacySharedDefaultsJson = "{}",
                RgbJson = "{\"IdleProfileName\":\"Dark White\"}"
            });

            var backup = ConfigBackupStore.Read(path);
            var rgb = System.Text.Json.JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson);

            Assert.IsNotNull(rgb);
            Assert.AreEqual("Dark White", rgb.IdleProfileName);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void FocusedBackup_RoundTripsWithoutUnrelatedSettings()
    {
        var path = TemporaryPath("advanced-backup.json");
        try
        {
            ConfigBackupStore.Write(path, new ConfigBackupDocument
            {
                Scope = BackupScope.AdvancedMode,
                AdvancedDefaultsJson = "{\"Milliseconds\":80}",
                AutomationProfilesJson = "{\"Profiles\":[]}"
            });

            var backup = ConfigBackupStore.Read(path);

            Assert.AreEqual(BackupScope.AdvancedMode, backup.Scope);
            Assert.AreEqual("{\"Milliseconds\":80}", backup.AdvancedDefaultsJson);
            Assert.IsTrue(string.IsNullOrEmpty(backup.SequenceLibraryJson));
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void BackupDocument_KeepsSimpleAndAdvancedDefaultsIndependent()
    {
        var path = TemporaryPath("separate-defaults.json");
        try
        {
            ConfigBackupStore.Write(path, new ConfigBackupDocument
            {
                Scope = BackupScope.Everything,
                SimpleDefaultsJson = "{\"Milliseconds\":35,\"InputPulseMilliseconds\":5}",
                AdvancedDefaultsJson = "{\"Milliseconds\":100,\"InputPulseMilliseconds\":2,\"InputJitterMaximumMilliseconds\":7}",
                AutomationProfilesJson = "{\"Profiles\":[{\"Name\":\"Game\",\"BehaviorDefaults\":{\"InputPulseMilliseconds\":3}}]}"
            });

            var backup = ConfigBackupStore.Read(path);

            Assert.AreNotEqual(backup.SimpleDefaultsJson, backup.AdvancedDefaultsJson);
            StringAssert.Contains(backup.SimpleDefaultsJson, "\"Milliseconds\":35");
            StringAssert.Contains(backup.AdvancedDefaultsJson, "\"InputJitterMaximumMilliseconds\":7");
            StringAssert.Contains(backup.AutomationProfilesJson, "\"InputPulseMilliseconds\":3");
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void ScopeRules_KeepResetAndBackupPartitionsSeparate()
    {
        Assert.IsTrue(SettingsScopeRules.ResetsSimple(ResetScope.SimpleMode));
        Assert.IsFalse(SettingsScopeRules.ResetsAdvancedProfiles(ResetScope.SimpleMode));
        Assert.IsFalse(SettingsScopeRules.ResetsAdvancedGlobals(ResetScope.SimpleMode));

        Assert.IsFalse(SettingsScopeRules.ResetsSimple(ResetScope.AdvancedMode));
        Assert.IsTrue(SettingsScopeRules.ResetsAdvancedProfiles(ResetScope.AdvancedMode));
        Assert.IsFalse(SettingsScopeRules.ResetsAdvancedGlobals(ResetScope.AdvancedMode));

        Assert.IsFalse(SettingsScopeRules.ResetsSimple(ResetScope.SharedDefaults));
        Assert.IsFalse(SettingsScopeRules.ResetsAdvancedProfiles(ResetScope.SharedDefaults));
        Assert.IsTrue(SettingsScopeRules.ResetsAdvancedGlobals(ResetScope.SharedDefaults));

        Assert.IsTrue(SettingsScopeRules.ResetsSimple(ResetScope.Everything));
        Assert.IsTrue(SettingsScopeRules.ResetsAdvancedProfiles(ResetScope.Everything));
        Assert.IsTrue(SettingsScopeRules.ResetsAdvancedGlobals(ResetScope.Everything));

        Assert.IsTrue(SettingsScopeRules.IncludesSimple(BackupScope.SimpleMode));
        Assert.IsFalse(SettingsScopeRules.IncludesAdvanced(BackupScope.SimpleMode));
        Assert.IsFalse(SettingsScopeRules.IncludesAppSettings(BackupScope.AdvancedMode));
        Assert.IsTrue(SettingsScopeRules.IncludesAdvanced(BackupScope.AdvancedMode));
        Assert.IsTrue(SettingsScopeRules.IncludesSequences(BackupScope.CustomSequences));
        Assert.IsTrue(SettingsScopeRules.IncludesSimple(BackupScope.Everything));
        Assert.IsTrue(SettingsScopeRules.IncludesAdvanced(BackupScope.Everything));
        Assert.IsTrue(SettingsScopeRules.IncludesSequences(BackupScope.Everything));
        Assert.IsTrue(SettingsScopeRules.IncludesAppSettings(BackupScope.Everything));
    }

    [TestMethod]
    public void BackupFileFilters_ShowOnlyAFocusedBackupAndCompleteBackupForFocusedRestore()
    {
        var simpleExport = BackupScopeInfo.ExportFilter(BackupScope.SimpleMode);
        var simpleImport = BackupScopeInfo.ImportFilter(BackupScope.SimpleMode);
        var everythingImport = BackupScopeInfo.ImportFilter(BackupScope.Everything);

        StringAssert.Contains(simpleExport, "*.autoclicker-simple.json");
        Assert.IsFalse(simpleExport.Contains("*.autoclicker-backup.json", StringComparison.Ordinal));
        StringAssert.Contains(simpleImport, "Supported backups");
        StringAssert.Contains(simpleImport, "*.autoclicker-simple.json;*.autoclicker-backup.json");
        StringAssert.Contains(simpleImport, "Simple mode settings only");
        StringAssert.Contains(everythingImport, "Complete AutoClicker backups");
        Assert.IsFalse(everythingImport.Contains("*.autoclicker-simple.json", StringComparison.Ordinal));
        Assert.AreEqual("AutoClicker-advanced-settings.autoclicker-advanced.json", BackupScopeInfo.DefaultFileName(BackupScope.AdvancedMode));
    }

    [TestMethod]
    public void FullBackupVersion2_RemainsReadableAsEverything()
    {
        var path = TemporaryPath("legacy-backup.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SchemaVersion\":2,\"DefaultsJson\":\"{}\"}");

            var backup = ConfigBackupStore.Read(path);

            Assert.AreEqual(BackupScope.Everything, backup.Scope);
            Assert.AreEqual("{}", backup.LegacySharedDefaultsJson);
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void FullBackupVersion3_MapsItsLegacyPreferenceFieldExplicitly()
    {
        var path = TemporaryPath("legacy-preferences-backup.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SchemaVersion\":3,\"Scope\":0,\"UiPreferencesJson\":\"{\\\"Pinned\\\":true}\"}");

            var backup = ConfigBackupStore.Read(path);

            Assert.AreEqual("{\"Pinned\":true}", backup.LegacyApplicationPreferencesJson);
            Assert.IsTrue(string.IsNullOrEmpty(backup.ApplicationPreferencesJson));
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    [TestMethod]
    public void FullBackup_RejectsFutureSchemas()
    {
        var path = TemporaryPath("future.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SchemaVersion\":99,\"DefaultsJson\":\"{}\"}");
            Assert.ThrowsException<InvalidDataException>(() => ConfigBackupStore.Read(path));
        }
        finally { DeleteTemporaryDirectory(path); }
    }

    private static string TemporaryPath(string name) => Path.Combine(Path.GetTempPath(), TestDirectoryName, Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat), name);
    private static void DeleteTemporaryDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
