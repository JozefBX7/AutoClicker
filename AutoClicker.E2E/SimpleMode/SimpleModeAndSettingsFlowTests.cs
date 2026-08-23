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
            app.SelectInput(AutomationInputLabels.RightClick);
            app.SelectActionType(AutomationActionTypeIds.Double);
            app.SetFiniteRepeat(17);
            app.SetFixedPosition(-123, 456);
            app.DisableTargetWindow();
            app.CaptureHotkey(VirtualKeyShort.F8);
            Assert.AreEqual(AutomationInputLabels.RightClick, app.SelectedInput);
            app.SaveAsDefault();
        }

        var stored = fixture.ReadSimpleDefaults();
        Assert.AreEqual(246, stored.Milliseconds);
        Assert.AreEqual(AutomationInputIds.Right, stored.Input);
        Assert.AreEqual(AutomationActionTypeIds.Double, stored.ClickType);
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
        Assert.AreEqual(AutomationInputLabels.RightClick, restartedApp.SelectedInput);
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

        var preferences = fixture.ReadApplicationPreferences();
        Assert.IsTrue(preferences.Pinned);
        Assert.IsTrue(preferences.CompactMode);
        Assert.IsTrue(preferences.AdvancedMode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.ReadAppearance()));

        using var restarted = fixture.Launch();
        Assert.IsTrue(fixture.ReadApplicationPreferences().AdvancedMode);
        Assert.AreEqual("Advanced", restarted.MainElement("Mode").Name);
        Assert.IsTrue(restarted.IsMainWindowTopmost, "remembered pinning should apply immediately by default");
    }

    [TestMethod]
    public void MainWindowPosition_PersistsAcrossACleanRestart()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        int expectedLeft;
        int expectedTop;
        using (var session = fixture.Launch())
        {
            var initial = session.MainWindowBounds;
            expectedLeft = initial.Left + 48;
            expectedTop = initial.Top + 32;
            session.MoveMainWindow(expectedLeft, expectedTop);
            session.CloseMainWindow();
        }

        var stored = fixture.ReadApplicationPreferences().MainWindowPosition;
        Assert.IsNotNull(stored, "a clean close did not persist the main-window position");
        Assert.AreEqual(expectedLeft, stored.Left);
        Assert.AreEqual(expectedTop, stored.Top);

        using var restarted = fixture.Launch();
        Assert.IsTrue(Math.Abs(restarted.MainWindowBounds.Left - expectedLeft) <= 1,
            $"the restarted window left edge was {restarted.MainWindowBounds.Left}, expected {expectedLeft}");
        Assert.IsTrue(Math.Abs(restarted.MainWindowBounds.Top - expectedTop) <= 1,
            $"the restarted window top edge was {restarted.MainWindowBounds.Top}, expected {expectedTop}");
    }

    [TestMethod]
    public void PinnedPreference_CanDeferUntilInteractionOrBeForgotten()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        fixture.WriteApplicationPreferences(new ApplicationPreferences
        {
            Pinned = true,
            RememberPinned = true,
            ApplyPinnedOnLaunch = false,
            QuickStartSeen = true
        });

        using (var session = fixture.Launch())
        {
            Assert.IsFalse(session.IsMainWindowTopmost, "the deferred remembered pin should not apply during launch");
            session.Window.Focus();
            FlaUI.Core.Input.Keyboard.Press(VirtualKeyShort.TAB);
            session.WaitFor(() => session.IsMainWindowTopmost, "the remembered pin did not apply after real main-window input");

            var app = new MainWindowRobot(session);
            app.OpenSettings();
            var settings = new SettingsRobot(session);
            settings.SetRememberPinned(false);
            Assert.IsFalse(settings.ApplyPinnedOnLaunchEnabled);
            settings.Save();
            session.WaitFor(() => !fixture.ReadApplicationPreferences().RememberPinned,
                "the disabled pinned preference was not persisted");
        }

        var forgotten = fixture.ReadApplicationPreferences();
        Assert.IsFalse(forgotten.RememberPinned);
        Assert.IsFalse(forgotten.Pinned);

        using var restarted = fixture.Launch();
        Assert.IsFalse(restarted.IsMainWindowTopmost);
        restarted.Window.Focus();
        FlaUI.Core.Input.Keyboard.Press(VirtualKeyShort.TAB);
        Thread.Sleep(100);
        Assert.IsFalse(restarted.IsMainWindowTopmost, "a forgotten pin should not be restored after interaction");
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
            session.WaitFor(() => fixture.ReadApplicationPreferences().AdvancedMode, "saved Settings mode was not persisted");
        }

        var preferences = fixture.ReadApplicationPreferences();
        Assert.IsTrue(preferences.AdvancedMode);
        Assert.IsTrue(preferences.KeyboardHotkeyModifiersEnabled);
        Assert.AreEqual("AboveNormal", preferences.WorkerPriority);
        Assert.IsTrue(preferences.CadenceDiagnosticsEnabled);
        Assert.IsFalse(preferences.CrashRecoveryEnabled);
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

    [TestMethod]
    public void Settings_AboutConfirmationLoadsPackagedWindowResourcesWithoutCrashing()
    {
        using var fixture = new ProfileE2EFixture(advancedMode: false);
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        app.OpenSettings();
        var settings = new SettingsRobot(session);

        settings.OpenAbout();
        var confirmation = session.Dialog("Confirmation");
        confirmation.FindFirstDescendant(condition => condition.ByAutomationId("ConfirmButton"))!.AsButton().Invoke();

        Assert.IsFalse(session.Application.HasExited);
        settings.Cancel();
        session.WaitFor(() => !session.IsDialogOpen("Settings"), "Settings did not close after its Cancel button was invoked");
    }
}
