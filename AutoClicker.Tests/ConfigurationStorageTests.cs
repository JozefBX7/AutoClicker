using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class ConfigurationStorageTests
{
    [TestMethod]
    public void SequenceLibrary_RoundTripsNamedPresetsAndSteps()
    {
        var path = TemporaryPath("sequence-library.json");
        try
        {
            SequenceLibraryStore.Save(path, [new SequencePreset { Name = "Combat", Steps = [new SequenceStep { Input = "Left" }, new SequenceStep { Input = "Space", DelayAfterMilliseconds = 80 }] }]);
            var library = SequenceLibraryStore.Load(path);
            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("Combat", library[0].Name);
            Assert.AreEqual(2, library[0].Steps.Count);
        }
        finally { DeleteTemporaryDirectory(path); }
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
            ConfigBackupStore.Write(path, new ConfigBackupDocument { DefaultsJson = "{}", SequenceLibraryJson = sequenceLibraryJson });

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
    }

    [TestMethod]
    public void SequenceLibrary_RoundTripsAllSupportedStepTypes()
    {
        var path = TemporaryPath("all-sequence-steps.json");
        var steps = new[]
        {
            new SequenceStep { Input = "Left", DelayAfterMilliseconds = 10 },
            new SequenceStep { Input = "Right", DelayAfterMilliseconds = 20 },
            new SequenceStep { Input = "Middle", DelayAfterMilliseconds = 30 },
            new SequenceStep { Input = "Space", DelayAfterMilliseconds = 40 },
            new SequenceStep { Input = "Enter", DelayAfterMilliseconds = 50 },
            new SequenceStep { Input = "Custom", CustomKey = 65, DelayAfterMilliseconds = 60 },
            new SequenceStep { Input = "Delay", DelayAfterMilliseconds = 70 }
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
            ConfigBackupStore.Write(path, new ConfigBackupDocument { DefaultsJson = "{}", SequenceLibraryJson = "{\"SchemaVersion\":1,\"Presets\":[]}" });
            var backup = ConfigBackupStore.Read(path);
            Assert.AreEqual(ConfigBackupStore.CurrentSchemaVersion, backup.SchemaVersion);
            Assert.AreEqual("{}", backup.DefaultsJson);
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

    private static string TemporaryPath(string name) => Path.Combine(Path.GetTempPath(), "AutoClicker.Tests", Guid.NewGuid().ToString("N"), name);
    private static void DeleteTemporaryDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
