namespace AutoClicker;

public sealed class SequenceStep
{
    public string Input { get; set; } = "Left";
    public int CustomKey { get; set; }
    public int DelayAfterMilliseconds { get; set; }

    public SequenceStep Clone() => new() { Input = Input, CustomKey = CustomKey, DelayAfterMilliseconds = DelayAfterMilliseconds };

    public override string ToString() => Input == "Delay"
        ? $"Wait {DelayAfterMilliseconds:N0} ms"
        : DelayAfterMilliseconds > 0 ? $"{Describe()}  → wait {DelayAfterMilliseconds:N0} ms" : Describe();

    public string Describe() => Input switch
    {
        "Left" => "Left click",
        "Right" => "Right click",
        "Middle" => "Middle click",
        "Space" => "Space",
        "Enter" => "Enter",
        "Custom" => System.Windows.Input.KeyInterop.KeyFromVirtualKey(CustomKey).ToString(),
        "Delay" => "Wait",
        _ => Input
    };
}
