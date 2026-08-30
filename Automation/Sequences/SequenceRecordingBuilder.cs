// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal sealed class SequenceRecordingBuilder(bool includeDelays, bool treatBriefTapsAsPresses = false)
{
    internal const int BriefTapMaximumMilliseconds = 160;
    private const int MaximumDelayMilliseconds = 600_000;
    private readonly List<SequenceStep> steps = [];
    private readonly Dictionary<SequenceInputIdentity, HeldRecording> held = [];
    private readonly List<SequenceInputIdentity> heldOrder = [];
    private long? lastEventAtMilliseconds;

    internal int TransitionCount { get; private set; }

    internal bool IsHeld(string input, int customKey = 0) => held.ContainsKey(Identity(input, customKey));

    internal bool Record(string input, int customKey, bool isDown, long timestampMilliseconds)
    {
        if (!IsSupportedStatefulInput(input, customKey)) return false;
        var identity = Identity(input, customKey);
        if (isDown)
        {
            if (held.ContainsKey(identity)) return false;
            MarkInterveningInput(identity);
            AppendDelay(timestampMilliseconds);
            var stepIndex = steps.Count;
            steps.Add(new SequenceStep { Input = input, CustomKey = customKey, Mode = SequenceStepMode.Hold });
            held.Add(identity, new HeldRecording(stepIndex, timestampMilliseconds));
            heldOrder.Add(identity);
        }
        else
        {
            if (!held.Remove(identity, out var heldRecording)) return false;
            MarkInterveningInput(identity);
            var duration = Math.Max(0, timestampMilliseconds - heldRecording.StartedAtMilliseconds);
            var briefTap = treatBriefTapsAsPresses
                && duration <= BriefTapMaximumMilliseconds
                && !heldRecording.HadInterveningInput;
            var immediateMouseClick = input != AutomationInputIds.Custom
                && !heldRecording.HadInterveningInput
                && (!includeDelays || duration == 0)
                && steps.Count == heldRecording.StepIndex + 1;
            if (briefTap || immediateMouseClick)
            {
                steps.RemoveRange(heldRecording.StepIndex, steps.Count - heldRecording.StepIndex);
                steps.Add(new SequenceStep { Input = input, CustomKey = customKey });
            }
            else
            {
                AppendDelay(timestampMilliseconds);
                steps.Add(new SequenceStep { Input = input, CustomKey = customKey, Mode = SequenceStepMode.Release });
            }
            heldOrder.Remove(identity);
        }

        lastEventAtMilliseconds = timestampMilliseconds;
        TransitionCount++;
        return true;
    }

    internal bool RecordPress(string input, long timestampMilliseconds)
    {
        if (!InputRules.IsInstantaneousMouseAction(input)) return false;
        foreach (var active in held.Values) active.HadInterveningInput = true;
        AppendDelay(timestampMilliseconds);
        steps.Add(new SequenceStep { Input = input });
        lastEventAtMilliseconds = timestampMilliseconds;
        TransitionCount++;
        return true;
    }

    internal void Complete(long timestampMilliseconds)
    {
        foreach (var identity in heldOrder.AsEnumerable().Reverse().ToArray())
        {
            held[identity].HadInterveningInput = true;
            Record(identity.Input, identity.CustomKey, isDown: false, timestampMilliseconds);
        }
    }

    internal IReadOnlyList<SequenceStep> Build() => steps.Select(step => step.Clone()).ToList();

    private void MarkInterveningInput(SequenceInputIdentity current)
    {
        foreach (var active in held)
            if (active.Key != current) active.Value.HadInterveningInput = true;
    }

    private void AppendDelay(long timestampMilliseconds)
    {
        if (!includeDelays || lastEventAtMilliseconds is not long previous) return;
        var remaining = Math.Max(0, timestampMilliseconds - previous);
        while (remaining > 0)
        {
            var delay = (int)Math.Min(remaining, MaximumDelayMilliseconds);
            steps.Add(new SequenceStep { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = delay });
            remaining -= delay;
        }
    }

    private static SequenceInputIdentity Identity(string input, int customKey) =>
        new(input, string.Equals(input, AutomationInputIds.Custom, StringComparison.Ordinal) ? customKey : 0);

    private static bool IsSupportedStatefulInput(string input, int customKey) => input switch
    {
        AutomationInputIds.Left or AutomationInputIds.Right or AutomationInputIds.Middle or AutomationInputIds.Mouse4 or AutomationInputIds.Mouse5 => true,
        AutomationInputIds.Custom => customKey is > 0 and <= 0xFF,
        _ => false
    };

    private sealed class HeldRecording(int stepIndex, long startedAtMilliseconds)
    {
        internal int StepIndex { get; } = stepIndex;
        internal long StartedAtMilliseconds { get; } = startedAtMilliseconds;
        internal bool HadInterveningInput { get; set; }
    }
}
