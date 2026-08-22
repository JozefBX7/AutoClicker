// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void InstalledConfigDirectory_UsesTheApplicationFolder() =>
        Assert.AreEqual(Path.Combine("C:", "Users", "Example", "AppData", "Local", "AutoClicker"), AppPaths.InstalledConfigDirectory(Path.Combine("C:", "Users", "Example", "AppData", "Local")));

    [TestMethod]
    public void PortableConfigDirectory_StaysBesideThePortableExecutable() =>
        Assert.AreEqual(Path.Combine("D:", "Tools", "AutoClicker", "Data"), AppPaths.PortableConfigDirectory(Path.Combine("D:", "Tools", "AutoClicker")));
}
