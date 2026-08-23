// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AppRuntimeTests
{
    [TestMethod]
    public void KeyboardHotkeyRegistration_RequiresBothEndToEndModeAndTheExplicitOptIn()
    {
        Assert.IsFalse(AppRuntime.ShouldRegisterEndToEndKeyboardHotkeys([]));
        Assert.IsFalse(AppRuntime.ShouldRegisterEndToEndKeyboardHotkeys([AppCommandLineOptions.EndToEnd]));
        Assert.IsFalse(AppRuntime.ShouldRegisterEndToEndKeyboardHotkeys([AppCommandLineOptions.RegisterEndToEndKeyboardHotkeys]));
        Assert.IsTrue(AppRuntime.ShouldRegisterEndToEndKeyboardHotkeys([AppCommandLineOptions.EndToEnd, AppCommandLineOptions.RegisterEndToEndKeyboardHotkeys]));
    }

    [TestMethod]
    public void EndToEndFilePathPolicy_AcceptsOnlyDescendantsOfTheIsolatedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoClicker.E2E", "isolated");

        Assert.IsTrue(AppRuntime.IsPathWithinDirectory(root, Path.Combine(root, "backup.json")));
        Assert.IsTrue(AppRuntime.IsPathWithinDirectory(root, Path.Combine(root, "nested", "profile.json")));
        Assert.IsFalse(AppRuntime.IsPathWithinDirectory(root, root));
        Assert.IsFalse(AppRuntime.IsPathWithinDirectory(root, Path.Combine(root, "..", "outside.json")));
        Assert.IsFalse(AppRuntime.IsPathWithinDirectory(root, root + "-sibling" + Path.DirectorySeparatorChar + "backup.json"));
    }
}
