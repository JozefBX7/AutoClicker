// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class WindowResourceReferenceTests
{
    private const string ApplicationIconUri = "/AutoClicker;component/Assets/AutoClickerIcon.ico";

    [TestMethod]
    public void WindowIcons_UseAnApplicationRootedPackUri()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var windowFiles = Directory.EnumerateFiles(repositoryRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));
        var iconReferences = windowFiles
            .Select(path => (Path: path, Root: XDocument.Load(path).Root))
            .Where(item => item.Root?.Name.LocalName == "Window")
            .Select(item => (item.Path, Icon: item.Root!.Attribute("Icon")?.Value))
            .Where(item => item.Icon is not null)
            .ToArray();

        Assert.IsTrue(iconReferences.Length > 0, "No window icon references were found.");
        foreach (var reference in iconReferences)
            Assert.AreEqual(ApplicationIconUri, reference.Icon, $"Window '{Path.GetRelativePath(repositoryRoot, reference.Path)}' uses a location-relative icon URI.");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
