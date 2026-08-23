// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

internal readonly record struct SequenceDragRow(int Index, double Midpoint);

internal static class SequenceEditorDragRules
{
    internal static int ResolveInsertionIndex(
        double pointerY,
        IReadOnlyList<SequenceDragRow> realizedRows,
        int itemCount)
    {
        if (itemCount <= 0) return 0;

        foreach (var row in realizedRows)
            if (pointerY < row.Midpoint)
                return Math.Clamp(row.Index, 0, itemCount);

        return realizedRows.Count == 0
            ? itemCount
            : Math.Clamp(realizedRows[^1].Index + 1, 0, itemCount);
    }
}
