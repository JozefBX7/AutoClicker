// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace AutoClicker.Tests;

[TestClass]
public sealed class AutomationHotkeyBindingTests
{
    [TestMethod]
    public void KeyboardConflicts_RespectTheModifierPreference()
    {
        var controlF6 = Binding(117, 0x2);
        var shiftF6 = Binding(117, 0x4);
        var altF6 = Binding(117, 0x1);

        Assert.IsFalse(AutomationHotkeyBindingRules.Conflicts(controlF6, shiftF6, keyboardModifiersEnabled: true));
        Assert.IsTrue(AutomationHotkeyBindingRules.Conflicts(controlF6, shiftF6, keyboardModifiersEnabled: false));
        Assert.IsFalse(AutomationHotkeyBindingRules.Conflicts(controlF6, altF6, keyboardModifiersEnabled: false));
    }

    [TestMethod]
    public void ProfileAssignments_IncludeRunAndEnableToggleBindingsButExcludeOnlyTheBindingBeingEdited()
    {
        var action = Action("one", 117, 120);
        var other = Action("two", 118, 121);
        var profile = new AutomationProfile { Actions = [action, other] };

        Assert.IsFalse(AutomationHotkeyBindingRules.IsAssigned(profile, Binding(117), true, action.Id, AutomationHotkeyAssignmentKind.RunAction));
        Assert.IsTrue(AutomationHotkeyBindingRules.IsAssigned(profile, Binding(120), true, action.Id, AutomationHotkeyAssignmentKind.RunAction));
        Assert.IsFalse(AutomationHotkeyBindingRules.IsAssigned(profile, Binding(120), true, action.Id, AutomationHotkeyAssignmentKind.ToggleEnabled));
        Assert.IsTrue(AutomationHotkeyBindingRules.IsAssigned(profile, Binding(118), true, action.Id, AutomationHotkeyAssignmentKind.ToggleEnabled));
        Assert.IsTrue(AutomationHotkeyBindingRules.IsAssigned(profile, Binding(121), true, action.Id, AutomationHotkeyAssignmentKind.ToggleEnabled));
    }

    [TestMethod]
    public void Enabling_StartsOnlyAnEnabledWhileHeldActionWhoseTriggerIsAlreadyDown()
    {
        var action = Action("held", 117, 120);
        action.Settings.ClickType = AutomationActionTypeIds.WhileHeld;

        Assert.IsTrue(AutomationHotkeyBindingRules.ShouldActivateWhileHeldOnEnable(action, _ => true));
        Assert.IsFalse(AutomationHotkeyBindingRules.ShouldActivateWhileHeldOnEnable(action, _ => false));
        action.HotkeyEnabled = false;
        Assert.IsFalse(AutomationHotkeyBindingRules.ShouldActivateWhileHeldOnEnable(action, _ => true));
        action.HotkeyEnabled = true;
        action.Settings.ClickType = AutomationActionTypeIds.Single;
        Assert.IsFalse(AutomationHotkeyBindingRules.ShouldActivateWhileHeldOnEnable(action, _ => true));
    }

    [TestMethod]
    public void ActionCloneAndJson_PreserveTheEnableToggleBinding()
    {
        var action = Action("persisted", 117, 120);

        var clone = action.Clone();
        var restored = JsonSerializer.Deserialize<AutomationAction>(JsonSerializer.Serialize(action));

        Assert.AreEqual(120, clone.EnableToggleHotkey?.VirtualKey);
        Assert.AreNotSame(action.EnableToggleHotkey, clone.EnableToggleHotkey);
        Assert.AreEqual(120, restored?.EnableToggleHotkey?.VirtualKey);
    }

    [TestMethod]
    public void GeneratedInput_CannotFireTheRunOrEnableToggleBinding()
    {
        var action = Action("safe", 117, 120);

        Assert.IsTrue(AutomationHotkeyBindingRules.ActionEmitsOwnKeyboardBinding(action, new AppDefaults { Input = AutomationInputIds.Custom, CustomKey = 117 }));
        Assert.IsTrue(AutomationHotkeyBindingRules.ActionEmitsOwnKeyboardBinding(action, new AppDefaults { Input = AutomationInputIds.Sequence, CustomSequence = [new SequenceStep { Input = AutomationInputIds.Custom, CustomKey = 120 }] }));
        Assert.IsFalse(AutomationHotkeyBindingRules.ActionEmitsOwnKeyboardBinding(action, new AppDefaults { Input = AutomationInputIds.Custom, CustomKey = 121 }));
    }

    [TestMethod]
    public void ProfileCopy_TreatsAnEnableToggleCollisionAsABindingConflict()
    {
        var destination = new AutomationProfile { Actions = [Action("destination", 120, 121)] };
        var source = Action("source", 117, 120);

        var result = AutomationProfileCopy.CopyTo(destination, [source], ProfileCopyConflictResolution.Skip);

        Assert.AreEqual(0, result.CopiedCount);
        Assert.AreEqual(1, result.SkippedCount);
        Assert.AreEqual("destination", destination.Actions.Single().Id);
    }

    [TestMethod]
    public void WindowsRegistration_AlwaysSuppressesKeyRepeatWithoutDroppingConfiguredModifiers()
    {
        const uint controlAndShift = 0x2 | 0x4;

        var registered = MainWindow.WindowsHotkeyRegistrationModifiers(controlAndShift);

        Assert.AreEqual(controlAndShift, registered & controlAndShift);
        Assert.AreNotEqual(0u, registered & 0x4000);
    }

    private static AutomationAction Action(string id, int runKey, int enableToggleKey) => new()
    {
        Id = id,
        Settings = new AppDefaults { Hotkey = runKey, HotkeyTrigger = HotkeyTrigger.Keyboard },
        EnableToggleHotkey = Binding(enableToggleKey)
    };

    private static AutomationHotkeyBinding Binding(int virtualKey, uint modifiers = 0) => new()
    {
        VirtualKey = virtualKey,
        Modifiers = modifiers,
        Trigger = HotkeyTrigger.Keyboard
    };
}
