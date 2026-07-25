using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    public void RgbSettings_DefaultsAreSafeAndPulseIsCaseInsensitive()
    {
        var settings = new RgbSettings();
        Assert.IsTrue(settings.CrashRecoveryEnabled);
        Assert.IsTrue(settings.StopAutoStartedOnExit);
        Assert.IsFalse(settings.IsPulse);

        settings.LightingEffect = "pUlSe";
        Assert.IsTrue(settings.IsPulse);
    }

    [TestMethod]
    public void KeyboardDevice_UsesItsNameForDisplay() =>
        Assert.AreEqual("Corsair K70 RGB", new KeyboardDevice(4, "Corsair K70 RGB").ToString());

    [TestMethod]
    public void SelectKeyboard_PrefersCaseInsensitiveNameOverDeviceIndex()
    {
        var selected = OpenRgbHighlighter.SelectKeyboard(
            [new KeyboardDevice(1, "Corsair K70 RGB"), new KeyboardDevice(2, "Other keyboard")],
            new RgbSettings { DeviceIndex = 2, DeviceName = "corsair k70 rgb" });

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
}
