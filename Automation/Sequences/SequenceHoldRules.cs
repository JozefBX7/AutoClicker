// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal static class SequenceHoldRules
{
    internal static string? ValidationError(IEnumerable<SequenceStep> sequence)
    {
        var held = new Dictionary<SequenceInputIdentity, string>();
        foreach (var step in sequence)
        {
            if (!IsSupportedInput(step)) return $"{step.Input} is not a supported sequence input.";
            if (step.Input == AutomationInputIds.Delay)
            {
                if (step.Mode != SequenceStepMode.Press) return "Wait events cannot be changed to Hold or Release.";
                continue;
            }

            if (!Enum.IsDefined(step.Mode)) return $"{step.Describe()} has an unsupported event mode.";
            var identity = Identity(step);
            var description = step.Describe();
            switch (step.Mode)
            {
                case SequenceStepMode.Hold when held.ContainsKey(identity):
                    return $"{description} is already held. Release it before holding it again.";
                case SequenceStepMode.Hold:
                    held.Add(identity, description);
                    break;
                case SequenceStepMode.Release when !held.Remove(identity):
                    return $"Release {description} must come after a matching Hold event.";
                case SequenceStepMode.Press when held.ContainsKey(identity):
                    return $"{description} is still held. Release it before adding a normal press.";
            }
        }

        return held.Count == 0
            ? null
            : $"Add a Release event for {held.Values.First()} before the sequence ends.";
    }

    internal static bool ContainsHold(IEnumerable<SequenceStep> sequence) =>
        sequence.Any(step => step.Input != AutomationInputIds.Delay && step.Mode == SequenceStepMode.Hold);

    internal static SequenceInputIdentity Identity(SequenceStep step) =>
        new(step.Input, string.Equals(step.Input, AutomationInputIds.Custom, StringComparison.Ordinal) ? step.CustomKey : 0);

    private static bool IsSupportedInput(SequenceStep step) => step.Input switch
    {
        AutomationInputIds.Left or AutomationInputIds.Right or AutomationInputIds.Middle or AutomationInputIds.Space or AutomationInputIds.Enter or AutomationInputIds.Delay => true,
        AutomationInputIds.Custom => step.CustomKey is > 0 and <= 0xFF,
        _ => false
    };
}

internal readonly record struct SequenceInputIdentity(string Input, int CustomKey);
