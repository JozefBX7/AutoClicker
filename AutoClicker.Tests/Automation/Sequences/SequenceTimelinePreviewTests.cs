// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceTimelinePreviewTests
{
    [TestMethod]
    public void Build_TracksExplicitTimingAndOverlappingHeldSpans()
    {
        SequenceStep[] sequence =
        [
            Key(0x11, SequenceStepMode.Hold),
            new() { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = 100 },
            Key(0x41, SequenceStepMode.Hold),
            new() { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = 250 },
            Key(0x41, SequenceStepMode.Release),
            Key(0x11, SequenceStepMode.Release)
        ];

        var preview = SequenceTimelinePreview.Build(sequence);

        Assert.AreEqual(6, preview.EventCount);
        Assert.AreEqual(350, preview.ExplicitDurationMilliseconds);
        Assert.AreEqual(new SequenceHoldSpan("A", 100, 350), preview.HoldSpans[0]);
        Assert.AreEqual(0, preview.HoldSpans[1].StartMilliseconds);
        Assert.AreEqual(350, preview.HoldSpans[1].EndMilliseconds);
        StringAssert.Contains(preview.Describe(), "350 ms");
    }

    [TestMethod]
    public void Build_DescribesMissingReleaseWithoutExecutingAnything()
    {
        var preview = SequenceTimelinePreview.Build([new SequenceStep { Input = AutomationInputIds.Left, Mode = SequenceStepMode.Hold }]);

        Assert.IsNull(preview.HoldSpans.Single().EndMilliseconds);
        StringAssert.Contains(preview.Describe(), "release missing");
    }

    private static SequenceStep Key(int virtualKey, SequenceStepMode mode) =>
        new() { Input = AutomationInputIds.Custom, CustomKey = virtualKey, Mode = mode };
}
