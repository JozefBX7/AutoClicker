using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class HotkeyModifierVariantTests
{
    [TestMethod]
    public void KeyboardHotkeyModifierVariants_ModifiersEnabled_UsesOnlyConfiguredModifiers()
    {
        var variants = MainWindow.KeyboardHotkeyModifierVariants(modifiersEnabled: true, configuredModifiers: 0x7).ToArray();

        CollectionAssert.AreEqual(new uint[] { 0x7 }, variants);
    }

    [TestMethod]
    public void KeyboardHotkeyModifierVariants_ModifiersDisabled_ExpandsCtrlShiftVariantsForPlainKey()
    {
        var variants = MainWindow.KeyboardHotkeyModifierVariants(modifiersEnabled: false, configuredModifiers: 0x0).ToArray();

        CollectionAssert.AreEqual(new uint[] { 0x0, 0x2, 0x4, 0x6 }, variants);
    }

    [TestMethod]
    public void KeyboardHotkeyModifierVariants_ModifiersDisabled_PreservesAltAndExpandsCtrlShift()
    {
        var variants = MainWindow.KeyboardHotkeyModifierVariants(modifiersEnabled: false, configuredModifiers: 0x1).ToArray();

        CollectionAssert.AreEqual(new uint[] { 0x1, 0x3, 0x5, 0x7 }, variants);
    }
}
