// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceRecordingBuilderTests
{
    [TestMethod]
    public void Build_CollapsesABriefKeyTapIntoAPress()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: false, treatBriefTapsAsPresses: true);

        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 175);

        var step = recording.Build().Single();
        Assert.AreEqual(AutomationInputIds.Custom, step.Input);
        Assert.AreEqual(0x41, step.CustomKey);
        Assert.AreEqual(SequenceStepMode.Press, step.Mode);
    }

    [TestMethod]
    public void Build_OmitsAllTimingWhenDelaysAreDisabled()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: false, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 160);
        recording.Record(AutomationInputIds.Left, 0, isDown: true, 900);
        recording.Record(AutomationInputIds.Left, 0, isDown: false, 1_100);

        var steps = recording.Build();
        CollectionAssert.AreEqual(
            new[] { AutomationInputIds.Custom, AutomationInputIds.Left },
            steps.Select(step => step.Input).ToArray());
        Assert.IsTrue(steps.All(step => step.Mode == SequenceStepMode.Press));
    }

    [TestMethod]
    public void Build_IncludesTimingBetweenTransitionsWithoutLeadingOrTrailingIdleTime()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: false);

        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 1_000);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 1_080);
        recording.Record(AutomationInputIds.Left, 0, isDown: true, 1_200);
        recording.Record(AutomationInputIds.Left, 0, isDown: false, 1_240);
        recording.Complete(9_000);

        var steps = recording.Build();
        CollectionAssert.AreEqual(
            new[]
            {
                SequenceStepMode.Hold,
                SequenceStepMode.Press,
                SequenceStepMode.Release,
                SequenceStepMode.Press,
                SequenceStepMode.Hold,
                SequenceStepMode.Press,
                SequenceStepMode.Release
            },
            steps.Select(step => step.Mode).ToArray());
        CollectionAssert.AreEqual(
            new[] { AutomationInputIds.Custom, AutomationInputIds.Delay, AutomationInputIds.Custom, AutomationInputIds.Delay, AutomationInputIds.Left, AutomationInputIds.Delay, AutomationInputIds.Left },
            steps.Select(step => step.Input).ToArray());
        CollectionAssert.AreEqual(new[] { 80, 120, 40 }, steps.Where(step => step.Input == AutomationInputIds.Delay).Select(step => step.DelayAfterMilliseconds).ToArray());
    }

    [TestMethod]
    public void Record_IgnoresAutoRepeatAndUnmatchedReleasePackets()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);

        Assert.IsFalse(recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 10));
        Assert.IsTrue(recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 20));
        Assert.IsFalse(recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 30));
        Assert.IsTrue(recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 40));

        Assert.AreEqual(2, recording.TransitionCount);
        Assert.AreEqual("A", recording.Build().Single().ToString());
    }

    [TestMethod]
    public void Complete_ReleasesHeldInputsInReverseOrder()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Custom, 0x11, isDown: true, 10);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 20);

        recording.Complete(30);

        CollectionAssert.AreEqual(
            new[] { "Hold LeftCtrl", "Wait 10 ms", "Hold A", "Wait 10 ms", "Release A", "Release LeftCtrl" },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_SplitsDelaysLongerThanTheSequenceLimit()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true);
        recording.Record(AutomationInputIds.Left, 0, isDown: true, 0);
        recording.Record(AutomationInputIds.Left, 0, isDown: false, 600_001);

        CollectionAssert.AreEqual(
            new[] { 600_000, 1 },
            recording.Build().Where(step => step.Input == AutomationInputIds.Delay).Select(step => step.DelayAfterMilliseconds).ToArray());
    }

    [TestMethod]
    public void Build_RemovesABriefTapsInternalDelayWhenConversionIsEnabled()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 260);

        CollectionAssert.AreEqual(new[] { "A" }, recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_LeavesBriefTapConversionDisabledByDefault()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 200);

        CollectionAssert.AreEqual(
            new[] { "Hold A", "Wait 100 ms", "Release A" },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_PreservesABriefKeyTapAsAShortHoldWhenConversionIsDisabled()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: false);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 175);

        CollectionAssert.AreEqual(
            new[] { "Hold A", "Wait 75 ms", "Release A" },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_PreservesAKeyHoldLongerThanTheQuickTapThreshold()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 261);

        CollectionAssert.AreEqual(
            new[] { "Hold A", "Wait 161 ms", "Release A" },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_DoesNotFlattenAChordIntoIndependentPresses()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Custom, 0x11, isDown: true, 100);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: true, 110);
        recording.Record(AutomationInputIds.Custom, 0x41, isDown: false, 140);
        recording.Record(AutomationInputIds.Custom, 0x11, isDown: false, 160);

        CollectionAssert.AreEqual(
            new[] { "Hold LeftCtrl", "Wait 10 ms", "A", "Wait 20 ms", "Release LeftCtrl" },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_ConvertsABriefMouseButtonTapWhenEnabled()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true, treatBriefTapsAsPresses: true);
        recording.Record(AutomationInputIds.Mouse4, 0, isDown: true, 100);
        recording.Record(AutomationInputIds.Mouse4, 0, isDown: false, 260);

        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.Mouse4Click },
            recording.Build().Select(step => step.ToString()).ToArray());
    }

    [TestMethod]
    public void Build_RecordsScrollEventsAndTheirInterEventDelays()
    {
        var recording = new SequenceRecordingBuilder(includeDelays: true);
        Assert.IsTrue(recording.RecordPress(AutomationInputIds.ScrollUp, 100));
        Assert.IsTrue(recording.RecordPress(AutomationInputIds.ScrollDown, 225));
        Assert.IsFalse(recording.RecordPress(AutomationInputIds.Left, 300));

        CollectionAssert.AreEqual(
            new[] { AutomationInputLabels.ScrollUp, "Wait 125 ms", AutomationInputLabels.ScrollDown },
            recording.Build().Select(step => step.ToString()).ToArray());
    }
}
