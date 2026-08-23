// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace AutoClicker.E2E;

internal enum EditorScope
{
    Global,
    Profile,
    Hotkey
}

internal sealed class ProfileEditorRobot(UIA3Automation automation, Window window)
{
    internal void Select(EditorScope scope)
    {
        switch (scope)
        {
            case EditorScope.Profile:
                Button($"Profile_{ProfileE2EFixture.ProfileId}").Invoke();
                break;
            case EditorScope.Hotkey:
                Button($"Action_{ProfileE2EFixture.ActionId}").Invoke();
                break;
            default:
                HeaderBackdrop().Click();
                break;
        }
        WaitUntil(() => ScopeHint.Contains(ExpectedScopeText(scope), StringComparison.Ordinal), "editor scope did not change");
    }

    internal string ScopeHint => Element("EditorScopeHint").Properties.Name.ValueOrDefault ?? string.Empty;
    internal int Milliseconds => int.Parse(TextBox("IntervalMilliseconds").Text);
    internal bool MillisecondsHasKeyboardFocus => TextBox("IntervalMilliseconds").Properties.HasKeyboardFocus.ValueOrDefault;
    internal int RepeatCount => int.Parse(TextBox("RepeatCount").Text);
    internal int CursorX => int.Parse(TextBox("CursorX").Text);
    internal int CursorY => int.Parse(TextBox("CursorY").Text);
    internal string TargetExecutable => TextBox("TargetExecutable").Text;
    internal bool TargetWindowEnabled => Element("TargetWindowEnabled").AsCheckBox().IsChecked == true;

    internal void EnterMilliseconds(int value)
    {
        var field = TextBox("IntervalMilliseconds");
        field.Focus();
        field.Text = value.ToString();
        Keyboard.Press(VirtualKeyShort.RETURN);
        WaitUntil(() => !field.Properties.HasKeyboardFocus.ValueOrDefault, "Enter did not release interval focus");
    }

    internal void TypeMillisecondsWithoutSubmitting(int value)
    {
        var field = TextBox("IntervalMilliseconds");
        field.Focus();
        field.Text = value.ToString();
    }

    internal void OverrideInterval()
    {
        // WPF does not expose the decorative card Border through UI Automation. A real click on its disabled
        // interval field bubbles through the card's PreviewMouseLeftButtonDown override handler.
        TextBox("IntervalMilliseconds").Click();
        WaitUntil(() => TextBox("IntervalMilliseconds").IsEnabled, "interval override did not become editable");
    }

    internal void EnterRepeatCount(int value)
    {
        var mode = Element("RepeatCountMode").AsRadioButton();
        if (!mode.IsChecked) mode.Click();
        EnterAndSubmit(TextBox("RepeatCount"), value.ToString());
    }

    internal void EnterCursorPosition(int x, int y)
    {
        var mode = Element("FixedPositionMode").AsRadioButton();
        if (!mode.IsChecked) mode.Click();
        TextBox("CursorX").Text = x.ToString();
        EnterAndSubmit(TextBox("CursorY"), y.ToString());
    }

    internal void EnterTargetExecutable(string executable)
    {
        EnterAndSubmit(TextBox("TargetExecutable"), executable);
        var enabled = Element("TargetWindowEnabled").AsCheckBox();
        if (enabled.IsChecked != true) enabled.Click();
    }

    internal void ClickGlobalBackdrop() => Select(EditorScope.Global);

    internal void SaveProfile()
    {
        var save = WaitForElement("SaveProfile").AsButton();
        save.Invoke();
        WaitUntil(() => window.FindFirstDescendant(condition => condition.ByAutomationId("SaveProfile")) is null,
            "profile save indicator did not clear");
    }

    internal void DiscardSelectedProfileChanges()
    {
        Button($"Profile_{ProfileE2EFixture.ProfileId}").RightClick();
        var discard = WaitForDesktopElement("DiscardProfileChanges").AsMenuItem();
        discard.Invoke();
        WaitUntil(() => ScopeHint.Contains("profile behavior defaults", StringComparison.Ordinal),
            "discard did not restore profile edit scope");
    }

    private Button Button(string automationId) => WaitForElement(automationId).AsButton();
    private TextBox TextBox(string automationId) => WaitForElement(automationId).AsTextBox();
    private AutomationElement Element(string automationId) => WaitForElement(automationId);

    private AutomationElement HeaderBackdrop() => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Text).And(condition.ByName(AppIdentity.Name))),
        "the clickable main-window header backdrop was not found");

    private AutomationElement WaitForElement(string automationId) => WaitUntilNotNull(
        () => window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
        $"element '{automationId}' was not found");

    private AutomationElement WaitForDesktopElement(string automationId) => WaitUntilNotNull(
        () => automation.GetDesktop().FindFirstDescendant(condition => condition.ByAutomationId(automationId)
            .And(condition.ByProcessId(window.Properties.ProcessId.ValueOrDefault))),
        $"desktop element '{automationId}' was not found");

    private static void EnterAndSubmit(TextBox field, string value)
    {
        field.Focus();
        field.Text = value;
        Keyboard.Press(VirtualKeyShort.RETURN);
        WaitUntil(() => !field.Properties.HasKeyboardFocus.ValueOrDefault, "Enter did not release field focus");
    }

    private static T WaitUntilNotNull<T>(Func<T?> find, string failure) where T : class
    {
        T? result = null;
        WaitUntil(() => (result = find()) is not null, failure);
        return result!;
    }

    private static void WaitUntil(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(75);
        }
        throw new TimeoutException(failure);
    }

    private static string ExpectedScopeText(EditorScope scope) => scope switch
    {
        EditorScope.Profile => "profile behavior defaults",
        EditorScope.Hotkey => "action",
        _ => "global behavior defaults"
    };
}
