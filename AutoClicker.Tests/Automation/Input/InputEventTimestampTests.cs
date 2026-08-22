// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class InputEventTimestampTests
{
    [TestMethod]
    public void Elapsed_UsesTheDifferenceBetweenInputEventTimestamps() =>
        Assert.AreEqual(TimeSpan.FromMilliseconds(80), InputEventTimestamp.Elapsed(1_000, 1_080));

    [TestMethod]
    public void Elapsed_HandlesTheUnsignedTimestampWraparound() =>
        Assert.AreEqual(TimeSpan.FromMilliseconds(80), InputEventTimestamp.Elapsed(unchecked((int)0xffff_fff0), 0x40));
}
