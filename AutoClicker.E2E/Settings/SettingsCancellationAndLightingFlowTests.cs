// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.E2E;

[TestClass]
[DoNotParallelize]
public sealed class SettingsCancellationAndLightingFlowTests
{
    [TestMethod]
    public void Cancel_DiscardsAllEditedSettings_ThenSavePersistsLightingOptionsWithoutDeviceAccess()
    {
        using var fixture = new ProfileE2EFixture();
        using var session = fixture.Launch();
        var app = new MainWindowRobot(session);
        var initialRgb = fixture.ReadRgbSettings();

        app.OpenSettings();
        var settings = new SettingsRobot(session);
        Assert.IsFalse(settings.OpenRgbOptionsEnabled);
        Configure(settings);
        Assert.IsTrue(settings.OpenRgbOptionsEnabled);
        settings.SelectMode("Simple");
        settings.SetKeyboardModifiers(true);
        settings.SetCadenceDiagnostics(true);
        settings.Cancel();

        Assert.AreEqual("Advanced", app.Mode);
        var cancelledRgb = fixture.ReadRgbSettings();
        Assert.AreEqual(initialRgb.Enabled, cancelledRgb.Enabled);
        Assert.AreEqual(initialRgb.AutoStart, cancelledRgb.AutoStart);
        Assert.AreEqual(initialRgb.StopAutoStartedOnExit, cancelledRgb.StopAutoStartedOnExit);
        Assert.AreEqual(initialRgb.IndicatorColor, cancelledRgb.IndicatorColor);
        var cancelledUi = fixture.ReadUiPreferences();
        Assert.IsFalse(cancelledUi.KeyboardHotkeyModifiersEnabled);
        Assert.IsFalse(cancelledUi.CadenceDiagnosticsEnabled);

        app.OpenSettings();
        settings = new SettingsRobot(session);
        Configure(settings);
        settings.Save();
        session.WaitFor(() => fixture.ReadRgbSettings().Enabled, "OpenRGB settings were not persisted");

        var saved = fixture.ReadRgbSettings();
        Assert.IsTrue(saved.Enabled);
        Assert.IsTrue(saved.AutoStart);
        Assert.IsTrue(saved.StopAutoStartedOnExit);
        Assert.AreEqual("#123ABC", saved.IndicatorColor);
        Assert.AreEqual("Fade", saved.LightingEffect);
        Assert.AreEqual(0, fixture.ReadRuntimeEvents().Count,
            "editing OpenRGB settings must not interact with a device during E2E tests");
    }

    private static void Configure(SettingsRobot settings)
    {
        settings.SetOpenRgb(true);
        settings.SetOpenRgbAutoStart(true);
        settings.SetStopAutoStartedOpenRgb(true);
        settings.SetIndicatorColor("#123abc");
        settings.SelectLightingEffect("Pulse");
    }
}
