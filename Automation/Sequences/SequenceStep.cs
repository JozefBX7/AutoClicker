// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public enum SequenceStepMode
{
    Press,
    Hold,
    Release
}

public sealed class SequenceStep
{
    public string Input { get; set; } = AutomationInputIds.Left;
    public int CustomKey { get; set; }
    public int DelayAfterMilliseconds { get; set; }
    public SequenceStepMode Mode { get; set; }

    public SequenceStep Clone() => new() { Input = Input, CustomKey = CustomKey, DelayAfterMilliseconds = DelayAfterMilliseconds, Mode = Mode };

    public override string ToString() => Input == AutomationInputIds.Delay
        ? $"Wait {DelayAfterMilliseconds:N0} ms"
        : DelayAfterMilliseconds > 0 ? $"{DescribeEvent()}  → wait {DelayAfterMilliseconds:N0} ms" : DescribeEvent();

    public string DescribeEvent() => Mode switch
    {
        SequenceStepMode.Hold => $"Hold {DescribeHeldInput()}",
        SequenceStepMode.Release => $"Release {DescribeHeldInput()}",
        _ => Describe()
    };

    public string Describe() => Input switch
    {
        AutomationInputIds.Left => AutomationInputLabels.LeftClick,
        AutomationInputIds.Right => AutomationInputLabels.RightClick,
        AutomationInputIds.Middle => AutomationInputLabels.MiddleClick,
        AutomationInputIds.Space => AutomationInputIds.Space,
        AutomationInputIds.Enter => AutomationInputIds.Enter,
        AutomationInputIds.Custom => System.Windows.Input.KeyInterop.KeyFromVirtualKey(CustomKey).ToString(),
        AutomationInputIds.Delay => AutomationInputLabels.Wait,
        _ => Input
    };

    private string DescribeHeldInput() => Input switch
    {
        AutomationInputIds.Left => AutomationInputLabels.LeftMouse,
        AutomationInputIds.Right => AutomationInputLabels.RightMouse,
        AutomationInputIds.Middle => AutomationInputLabels.MiddleMouse,
        _ => Describe()
    };
}
