// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class SequenceEditorDragRulesTests
{
    private static readonly SequenceDragRow[] Rows =
    [
        new(0, 10),
        new(1, 30),
        new(2, 50)
    ];

    [DataTestMethod]
    [DataRow(0, 0)]
    [DataRow(9.9, 0)]
    [DataRow(10, 1)]
    [DataRow(29.9, 1)]
    [DataRow(30, 2)]
    [DataRow(49.9, 2)]
    [DataRow(50, 3)]
    [DataRow(100, 3)]
    public void ResolveInsertionIndex_ChangesOnlyWhenThePointerCrossesARowMidpoint(double pointerY, int expected) =>
        Assert.AreEqual(expected, SequenceEditorDragRules.ResolveInsertionIndex(pointerY, Rows, 3));

    [TestMethod]
    public void ResolveInsertionIndex_UsesVisibleIndicesWhenTheListIsScrolled()
    {
        SequenceDragRow[] visibleRows = [new(7, 10), new(8, 30), new(9, 50)];

        Assert.AreEqual(7, SequenceEditorDragRules.ResolveInsertionIndex(0, visibleRows, 20));
        Assert.AreEqual(10, SequenceEditorDragRules.ResolveInsertionIndex(60, visibleRows, 20));
    }

    [TestMethod]
    public void ResolveInsertionIndex_HandlesEmptyAndTemporarilyUnrealizedLists()
    {
        Assert.AreEqual(0, SequenceEditorDragRules.ResolveInsertionIndex(12, [], 0));
        Assert.AreEqual(4, SequenceEditorDragRules.ResolveInsertionIndex(12, [], 4));
    }
}
