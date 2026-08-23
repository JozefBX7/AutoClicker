// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class HotkeyHoldSafetyTests
{
    [DataTestMethod]
    [DataRow((int)HotkeyTrigger.Keyboard, 0x75)]
    [DataRow((int)HotkeyTrigger.MiddleMouse, 0x04)]
    [DataRow((int)HotkeyTrigger.Mouse4, 0x05)]
    [DataRow((int)HotkeyTrigger.Mouse5, 0x06)]
    public void PhysicalVirtualKey_MapsTriggersWithARealReleaseState(int triggerValue, int expected) =>
        Assert.AreEqual(expected, HotkeyHoldSafety.PhysicalVirtualKey((HotkeyTrigger)triggerValue, 0x75));

    [DataTestMethod]
    [DataRow((int)HotkeyTrigger.WheelUp)]
    [DataRow((int)HotkeyTrigger.WheelDown)]
    [DataRow((int)HotkeyTrigger.WheelLeft)]
    [DataRow((int)HotkeyTrigger.WheelRight)]
    public void PhysicalVirtualKey_RejectsWheelGesturesThatCannotBeHeld(int triggerValue) =>
        Assert.IsNull(HotkeyHoldSafety.PhysicalVirtualKey((HotkeyTrigger)triggerValue, 0x75));

    [TestMethod]
    public void RequiredKeyboardModifiers_PreservesExactModifiersOnlyWhenEnabled()
    {
        Assert.AreEqual(0x7u, HotkeyHoldSafety.RequiredKeyboardModifiers(true, 0x7));
        Assert.AreEqual(0x1u, HotkeyHoldSafety.RequiredKeyboardModifiers(false, 0x7));
    }

    [TestMethod]
    public void IsTriggerDown_RequiresTheTriggerAndEveryConfiguredModifier()
    {
        var pressed = new HashSet<int> { 0x75, 0x11, 0x10 };
        Assert.IsTrue(HotkeyHoldSafety.IsTriggerDown(0x75, 0x2 | 0x4, pressed.Contains));

        pressed.Remove(0x10);
        Assert.IsFalse(HotkeyHoldSafety.IsTriggerDown(0x75, 0x2 | 0x4, pressed.Contains));
        pressed.Add(0x10);
        pressed.Remove(0x75);
        Assert.IsFalse(HotkeyHoldSafety.IsTriggerDown(0x75, 0x2 | 0x4, pressed.Contains));
    }
}
