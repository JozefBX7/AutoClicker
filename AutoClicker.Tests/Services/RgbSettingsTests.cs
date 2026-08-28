// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace AutoClicker.Tests;

[TestClass]
public sealed class RgbSettingsTests
{
    [DataTestMethod]
    [DataRow("#22d3ee", "#22D3EE")]
    [DataRow("22D3EE", "#22D3EE")]
    [DataRow("  #abc  ", "#AABBCC")]
    [DataRow("#ffffff", "#FFFFFF")]
    public void NormalizeIndicatorColor_AcceptsSupportedHexForms(string value, string expected)
    {
        Assert.IsTrue(OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var normalized));
        Assert.AreEqual(expected, normalized);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("#12")]
    [DataRow("#12345G")]
    [DataRow("not-a-colour")]
    public void NormalizeIndicatorColor_RejectsInvalidValues(string value)
    {
        Assert.IsFalse(OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var normalized));
        Assert.AreEqual("#22D3EE", normalized);
    }

    [TestMethod]
    public void RgbSettings_LegacyBlinkIdentifierAndFadeEffectRemainDistinct()
    {
        var settings = new RgbSettings();
        Assert.IsTrue(settings.StopAutoStartedOnExit);
        Assert.AreEqual(string.Empty, settings.IdleProfileName);
        Assert.IsFalse(settings.UsesFadeEffect);

        settings.LightingEffect = "pUlSe";
        Assert.IsTrue(settings.UsesBlinkEffect);
        Assert.IsFalse(settings.UsesFadeEffect);

        settings.LightingEffect = "Fade";
        Assert.IsTrue(settings.UsesFadeEffect);
        Assert.IsFalse(settings.UsesBlinkEffect);
    }

    [TestMethod]
    public void RgbSettings_ClonePreservesIdleProfileName()
    {
        var source = new RgbSettings { IdleProfileName = "Dark White" };

        var clone = source.Clone();

        Assert.AreEqual("Dark White", clone.IdleProfileName);
    }

    [TestMethod]
    public void RgbSettings_EffectSpeedKeepsItsLegacyJsonNameForCompatibility()
    {
        var json = JsonSerializer.Serialize(new RgbSettings { EffectSpeedMilliseconds = 875 });
        var restored = JsonSerializer.Deserialize<RgbSettings>("{\"PulseSpeedMilliseconds\":925}");

        StringAssert.Contains(json, "\"PulseSpeedMilliseconds\":875");
        Assert.IsNotNull(restored);
        Assert.AreEqual(925, restored.EffectSpeedMilliseconds);
    }

    [DataTestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    public void ApplicationLaunch_StartsOpenRgbOnlyWhenLightingAndAutoStartAreEnabled(bool enabled, bool autoStart, bool expected)
    {
        Assert.AreEqual(expected, OpenRgbHighlighter.ShouldStartOnApplicationLaunch(new RgbSettings { Enabled = enabled, AutoStart = autoStart }));
    }

    [TestMethod]
    public void AutoStartedServer_BindsOnlyToLoopback()
    {
        var startInfo = OpenRgbHighlighter.CreateServerStartInfo(@"C:\Program Files\OpenRGB\OpenRGB.exe");

        CollectionAssert.AreEqual(new[] { "--server", "--server-host", "127.0.0.1", "--noautoconnect" }, startInfo.ArgumentList.ToArray());
        Assert.AreEqual(@"C:\Program Files\OpenRGB", startInfo.WorkingDirectory);
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsTrue(startInfo.CreateNoWindow);
    }

    [TestMethod]
    public void SdkServerDiagnostics_AllowsOneListenerAndDuplicateRowsFromTheSameProcess()
    {
        Assert.IsNull(OpenRgbServerDiagnostics.GetConflictMessage([42]));
        Assert.IsNull(OpenRgbServerDiagnostics.GetConflictMessage([42, 42]));
    }

    [TestMethod]
    public void SdkServerDiagnostics_ExplainsCompetingServerProcesses()
    {
        var message = OpenRgbServerDiagnostics.GetConflictMessage([84, 42, 84]);

        Assert.IsNotNull(message);
        StringAssert.Contains(message, "port 6742");
        StringAssert.Contains(message, "processes 42 and 84");
        StringAssert.Contains(message, "Windows service");
        StringAssert.Contains(message, "restart or rescan");
    }

    [TestMethod]
    public void ShutdownIdleProfileRestore_CannotAutoStartOpenRgb()
    {
        var source = new RgbSettings { Enabled = true, AutoStart = true, IdleProfileName = "Dark White" };

        var shutdownSettings = OpenRgbHighlighter.CreateIdleProfileSettings(source, allowAutoStart: false);

        Assert.IsTrue(shutdownSettings.Enabled);
        Assert.IsFalse(shutdownSettings.AutoStart);
        Assert.AreEqual("Dark White", shutdownSettings.IdleProfileName);
        Assert.IsTrue(source.AutoStart);
    }

    [DataTestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    public void WarningIndicator_ShowsOnlyWhileLightingIsEnabledAndTheAppIsOpen(bool lightingEnabled, bool applicationClosing, bool expected)
    {
        Assert.AreEqual(expected, OpenRgbWarningRules.ShouldDisplay(lightingEnabled, applicationClosing));
    }

    [TestMethod]
    public void BlendColor_InterpolatesAndClampsTheFadeStrength()
    {
        var baseColor = new OpenRGB.NET.Color(10, 20, 30);
        var indicator = new OpenRGB.NET.Color(110, 120, 130);

        Assert.AreEqual(new OpenRGB.NET.Color(10, 20, 30), OpenRgbHighlighter.BlendColor(baseColor, indicator, -1));
        Assert.AreEqual(new OpenRGB.NET.Color(60, 70, 80), OpenRgbHighlighter.BlendColor(baseColor, indicator, 0.5));
        Assert.AreEqual(new OpenRGB.NET.Color(110, 120, 130), OpenRgbHighlighter.BlendColor(baseColor, indicator, 2));
    }

    [TestMethod]
    public void KeyboardLightingTest_UsesTheSelectedColourForEveryExposedLed()
    {
        var selected = new OpenRGB.NET.Color(34, 211, 238);
        var colours = OpenRgbHighlighter.CreateKeyboardFlashColors([new OpenRGB.NET.Color(1, 2, 3), new OpenRGB.NET.Color(4, 5, 6), new OpenRGB.NET.Color(7, 8, 9)], selected);

        CollectionAssert.AreEqual(new[] { selected, selected, selected }, colours);
    }

    [TestMethod]
    public void KeyboardEffectTest_BlendsEveryExposedLed()
    {
        var colours = OpenRgbHighlighter.CreateKeyboardBlendColors([new OpenRGB.NET.Color(10, 20, 30), new OpenRGB.NET.Color(30, 40, 50)], new OpenRGB.NET.Color(110, 120, 130), 0.5);

        CollectionAssert.AreEqual(new[] { new OpenRGB.NET.Color(60, 70, 80), new OpenRGB.NET.Color(70, 80, 90) }, colours);
    }

    [TestMethod]
    public void Fade_UsesTwelveBlendStepsPerCycle() =>
        Assert.AreEqual(12, OpenRgbHighlighter.FadeFramesPerCycle);

    [TestMethod]
    public void Fade_UsesAResponsiveCycleRange()
    {
        Assert.AreEqual(600, OpenRgbHighlighter.MinimumFadeCycleMilliseconds);
        Assert.AreEqual(3500, OpenRgbHighlighter.MaximumFadeCycleMilliseconds);
    }

    [DataTestMethod]
    [DataRow(600, 12)]
    [DataRow(1200, 12)]
    [DataRow(1300, 13)]
    [DataRow(2500, 25)]
    [DataRow(3500, 35)]
    [DataRow(10000, 36)]
    public void Fade_FrameCountScalesSmoothlyAndIsCapped(int cycleMilliseconds, int expectedFrames)
    {
        Assert.AreEqual(expectedFrames, OpenRgbHighlighter.GetFadeFramesPerCycle(cycleMilliseconds));
    }

    [TestMethod]
    public void SolidPreview_StaysLitForFiveSeconds() =>
        Assert.AreEqual(5000, OpenRgbHighlighter.SolidPreviewDurationMilliseconds);

    [TestMethod]
    public void KeyboardDevice_UsesItsNameForDisplay() =>
        Assert.AreEqual("Aurora 9000", new KeyboardDevice(4, "Aurora 9000").ToString());

    [TestMethod]
    public void SelectKeyboard_PrefersCaseInsensitiveNameOverDeviceIndex()
    {
        var selected = OpenRgbHighlighter.SelectKeyboard(
            [new KeyboardDevice(1, "Aurora 9000"), new KeyboardDevice(2, "Other keyboard")],
            new RgbSettings { DeviceIndex = 2, DeviceName = "aurora 9000" });

        Assert.AreEqual(1, selected?.Index);
    }

    [TestMethod]
    public void SelectKeyboard_GracefullyFallsBackWhenThereIsOnlyOneKeyboard()
    {
        var selected = OpenRgbHighlighter.SelectKeyboard([new KeyboardDevice(9, "Replacement keyboard")], new RgbSettings { DeviceName = "Old keyboard" });
        Assert.AreEqual(9, selected?.Index);
    }

    [TestMethod]
    public void SelectKeyboard_DoesNotGuessBetweenSeveralDifferentKeyboards()
    {
        var selected = OpenRgbHighlighter.SelectKeyboard(
            [new KeyboardDevice(1, "Keyboard A"), new KeyboardDevice(2, "Keyboard B")],
            new RgbSettings { DeviceName = "Missing keyboard" });

        Assert.IsNull(selected);
    }

    [TestMethod]
    public void KeyboardDiscovery_TrustsOpenRgbKeyboardTypeWithoutRecognisingTheModel()
    {
        Assert.IsTrue(OpenRgbDeviceClassifier.IsKeyboard(
            OpenRGB.NET.DeviceType.Keyboard,
            "Unfamiliar Model 123",
            null,
            []));
    }

    [TestMethod]
    public void KeyboardDiscovery_DoesNotTreatAnyExplicitNonKeyboardTypeAsAKeyboard()
    {
        foreach (var type in Enum.GetValues<OpenRGB.NET.DeviceType>().Except([OpenRGB.NET.DeviceType.Keyboard, OpenRGB.NET.DeviceType.Unknown]))
        {
            Assert.IsFalse(OpenRgbDeviceClassifier.IsKeyboard(
                type,
                "Gaming Keyboard Accessory",
                "Keyboard-compatible lighting",
                ["Key: A", "Key: B", "Key: C", "Key: D", "Key: E", "Key: F", "Key: G", "Key: H", "Key: I", "Key: J", "Key: Enter", "Key: Tab", "Key: Escape"]),
                $"OpenRGB's explicit {type} classification must remain authoritative.");
        }
    }

    [DataTestMethod]
    [DataRow("Mechanical Keyboard", null)]
    [DataRow("Macro Keypad", null)]
    [DataRow("ACME KBD 84", null)]
    [DataRow("Unfamiliar Model", "USB RGB keyboard controller")]
    public void KeyboardDiscovery_UsesGenericClassLabelsWhenOpenRgbTypeIsUnknown(string name, string? description)
    {
        Assert.IsTrue(OpenRgbDeviceClassifier.IsKeyboard(OpenRGB.NET.DeviceType.Unknown, name, description, []));
    }

    [TestMethod]
    public void KeyboardDiscovery_RecognisesAnUnknownModelFromItsExposedKeyLayout()
    {
        string?[] ledNames =
        [
            "Key: A", "Key: B", "Key: C", "Key: D", "Key: E", "Key: F",
            "Key: G", "Key: H", "Key: I", "Key: J", "Key: K", "Key: L",
            "Key: 1", "Key: 2", "Key: 3", "Key: Escape", "Key: Tab", "Key: Enter"
        ];

        Assert.IsTrue(OpenRgbDeviceClassifier.IsKeyboard(OpenRGB.NET.DeviceType.Unknown, "Aurora 9000", null, ledNames));
    }

    [TestMethod]
    public void KeyboardDiscovery_DoesNotGuessFromSparseOrGenericUnknownLighting()
    {
        Assert.IsFalse(OpenRgbDeviceClassifier.IsKeyboard(
            OpenRGB.NET.DeviceType.Unknown,
            "RGB Controller 12",
            null,
            ["LED 1", "LED 2", "Logo", "Key: A", "Key: Enter"]));
    }

    [TestMethod]
    public void StuckLightingRecovery_RefreshesEveryCurrentLedColour()
    {
        var current = new[] { new OpenRGB.NET.Color(1, 2, 3), new OpenRGB.NET.Color(4, 5, 6) };
        var refreshed = OpenRgbHighlighter.CreateRecoveryColors(current);

        CollectionAssert.AreEqual(current, refreshed);
        Assert.AreNotSame(current, refreshed);
    }

    [TestMethod]
    public void RestoreIndicatorColor_ReplacesTheTemporaryKeyColourWithItsCapturedColour()
    {
        var original = new[] { new OpenRGB.NET.Color(1, 2, 3), new OpenRGB.NET.Color(4, 5, 6) };
        var mode = new RgbDeviceModeSnapshot(0, null, null, []);
        var snapshot = new RgbLightingSnapshot(1, original, mode, 1, new OpenRGB.NET.Color(34, 211, 238));

        var restored = OpenRgbHighlighter.RestoreIndicatorColor([original[0], snapshot.IndicatorColor], snapshot);

        CollectionAssert.AreEqual(original, restored);
    }
}
