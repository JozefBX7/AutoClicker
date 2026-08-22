// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class SimpleModeAndSettingsFlowTests
{
    [TestMethod]
    public void SimpleConfiguration_SetAsDefaultPersistsEveryCoreFieldAcrossRestart()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using (var session = fixture.Launch())
        {
            var app = new MainWindowRobot(session);
            app.SetIntervalMilliseconds(246);
            app.SelectInput("Right click");
            app.SelectActionType("Double");
            app.SetFiniteRepeat(17);
            app.SetFixedPosition(-123, 456);
            app.DisableTargetWindow();
            app.CaptureHotkey(VirtualKeyShort.F8);
            Assert.AreEqual("Right click", app.SelectedInput);
            app.SaveAsDefault();
        }

        var stored = fixture.ReadSimpleDefaults();
        Assert.AreEqual(246, stored.Milliseconds);
        Assert.AreEqual("Right", stored.Input);
        Assert.AreEqual("Double", stored.ClickType);
        Assert.IsFalse(stored.RepeatUntilStopped);
        Assert.AreEqual(17, stored.RepeatCount);
        Assert.IsTrue(stored.FixedPosition);
        Assert.AreEqual(-123, stored.X);
        Assert.AreEqual(456, stored.Y);
        Assert.IsFalse(stored.TargetWindowEnabled);
        Assert.AreEqual(119, stored.Hotkey);

        using var restarted = fixture.Launch();
        var restartedApp = new MainWindowRobot(restarted);
        Assert.AreEqual(246, restarted.Editor.Milliseconds);
        Assert.AreEqual("Right click", restartedApp.SelectedInput);
        Assert.AreEqual("F8", restarted.MainElement("HotkeyLabel").Name);
    }

    [TestMethod]
    public void WindowPreferences_ModePinThemeAndCompactStatePersist()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using (var session = fixture.Launch())
        {
            var app = new MainWindowRobot(session);
            app.TogglePin();
            app.ToggleTheme();
            app.CollapseSettings();
            app.SwitchMode();
        }

        var preferences = fixture.ReadUiPreferences();
        Assert.IsTrue(preferences.Pinned);
        Assert.IsTrue(preferences.CompactMode);
        Assert.IsTrue(preferences.AdvancedMode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.ReadAppearance()));

        using var restarted = fixture.Launch();
        Assert.IsTrue(fixture.ReadUiPreferences().AdvancedMode);
        Assert.AreEqual("Advanced", restarted.MainElement("Mode").Name);
    }

    [TestMethod]
    public void Settings_SaveAppliesModeHotkeyWorkerDiagnosticsAndCrashRecoveryPreferences()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using (var session = fixture.Launch())
        {
            var app = new MainWindowRobot(session);
            app.OpenSettings();
            var settings = new SettingsRobot(session);
            settings.SelectMode("Advanced");
            settings.SetKeyboardModifiers(true);
            settings.SelectWorkerPriority("Above Normal (compatibility)");
            settings.SetCadenceDiagnostics(true);
            settings.SetCrashRecovery(false);
            settings.Save();
            session.WaitFor(() => fixture.ReadUiPreferences().AdvancedMode, "saved Settings mode was not persisted");
        }

        var preferences = fixture.ReadUiPreferences();
        Assert.IsTrue(preferences.AdvancedMode);
        Assert.IsTrue(preferences.KeyboardHotkeyModifiersEnabled);
        Assert.AreEqual("AboveNormal", preferences.WorkerPriority);
        Assert.IsTrue(preferences.CadenceDiagnosticsEnabled);
        Assert.IsFalse(fixture.ReadRgbSettings().CrashRecoveryEnabled);
    }

    [TestMethod]
    public void Settings_QuickStartGuideCanOpenAndCloseWithoutCrashingEitherWindow()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);
        settings.OpenQuickStart();
        var quickStart = session.Dialog("Welcome to AutoClicker");
        quickStart.FindFirstDescendant(condition => condition.ByName("Get started"))!.AsButton().Invoke();
        settings.Cancel();
        Assert.IsFalse(session.Application.HasExited);
    }
}
