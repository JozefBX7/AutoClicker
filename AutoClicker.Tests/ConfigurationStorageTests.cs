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
