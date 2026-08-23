// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal sealed record SequenceHoldSpan(string Input, long StartMilliseconds, long? EndMilliseconds);

internal sealed record SequenceTimeline(int EventCount, long ExplicitDurationMilliseconds, IReadOnlyList<SequenceHoldSpan> HoldSpans)
{
    internal string Describe()
    {
        if (EventCount == 0) return "Add events to preview their order and timing.";
        var summary = $"{EventCount:N0} event{(EventCount == 1 ? string.Empty : "s")} · {FormatDuration(ExplicitDurationMilliseconds)} explicit timeline";
        if (HoldSpans.Count == 0) return summary + " · no held spans";

        var spans = HoldSpans.Take(3).Select(span => span.EndMilliseconds is long end
            ? $"{span.Input} {FormatDuration(span.StartMilliseconds)}–{FormatDuration(end)}"
            : $"{span.Input} held from {FormatDuration(span.StartMilliseconds)} (release missing)");
        var remaining = HoldSpans.Count > 3 ? $" · +{HoldSpans.Count - 3:N0} more" : string.Empty;
        return summary + Environment.NewLine + string.Join(" · ", spans) + remaining;
    }

    private static string FormatDuration(long milliseconds) => milliseconds < 1_000
        ? $"{milliseconds:N0} ms"
        : $"{milliseconds / 1_000d:0.###} s";
}

internal static class SequenceTimelinePreview
{
    internal static SequenceTimeline Build(IEnumerable<SequenceStep> sequence)
    {
        var steps = sequence.ToList();
        var elapsed = 0L;
        var active = new Dictionary<SequenceInputIdentity, (string Description, long StartedAt)>();
        var spans = new List<SequenceHoldSpan>();

        foreach (var step in steps)
        {
            if (step.Input == AutomationInputIds.Delay)
            {
                elapsed += Math.Clamp(step.DelayAfterMilliseconds, 1, 600000);
                continue;
            }

            var identity = SequenceHoldRules.Identity(step);
            if (step.Mode == SequenceStepMode.Hold && !active.ContainsKey(identity))
                active.Add(identity, (step.Describe(), elapsed));
            else if (step.Mode == SequenceStepMode.Release && active.Remove(identity, out var started))
                spans.Add(new SequenceHoldSpan(started.Description, started.StartedAt, elapsed));

            elapsed += Math.Clamp(step.DelayAfterMilliseconds, 0, 600000);
        }

        spans.AddRange(active.Values.Select(started => new SequenceHoldSpan(started.Description, started.StartedAt, null)));
        return new SequenceTimeline(steps.Count, elapsed, spans);
    }
}
