// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

/// <summary>Automation contracts shared by the sequence editor and its desktop tests.</summary>
public static class SequenceEditorAutomationIds
{
    public const string Window = "SequenceEditorWindow";
    public const string Steps = "SequenceSteps";
    public const string StepDelay = "SequenceStepDelay";
    public const string StepMode = "SequenceStepMode";
    public const string MoveUp = "MoveSequenceStepUp";
    public const string MoveDown = "MoveSequenceStepDown";
    public const string Remove = "RemoveSequenceStep";
    public const string TimelinePreview = "SequenceTimelinePreview";
    public const string RemoveSelected = "SequenceRemoveSelected";
    public const string DuplicateSelected = "SequenceDuplicateSelected";
    public const string CopySelected = "SequenceCopySelected";
    public const string Paste = "SequencePaste";
    public const string AddMatchingRelease = "SequenceAddMatchingRelease";
    public const string SelectAll = "SequenceSelectAll";
    public const string Record = "RecordSequence";
}

/// <summary>Automation contracts shared by the sequence recorder and its desktop tests.</summary>
public static class SequenceRecorderAutomationIds
{
    public const string Window = "SequenceRecorderWindow";
    public const string IncludeDelays = "SequenceRecorderIncludeDelays";
    public const string TreatBriefTapsAsPresses = "SequenceRecorderTreatBriefTapsAsPresses";
    public const string Start = "StartSequenceRecording";
    public const string Stop = "StopSequenceRecording";
    public const string Use = "UseSequenceRecording";
    public const string Status = "SequenceRecorderStatus";
    public const string EventCount = "SequenceRecorderEventCount";
    public const string RecordedEvents = "SequenceRecorderRecordedEvents";
    public const string DeleteRecordedEvents = "SequenceRecorderDeleteRecordedEvents";
    public const string DeleteRecordedEvent = "SequenceRecorderDeleteRecordedEvent";
    public const string ConfirmDeleteRecordedEvent = "SequenceRecorderConfirmDeleteRecordedEvent";
    public const string CancelDeleteRecordedEvent = "SequenceRecorderCancelDeleteRecordedEvent";
}
