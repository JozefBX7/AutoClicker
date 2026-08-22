// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoClicker.Tests;

[TestClass]
public sealed class CrashRecoveryTests
{
    [DataTestMethod]
    [DataRow(0xAC71)]
    [DataRow(unchecked((int)0xE0434352))]
    [DataRow(unchecked((int)0x80131506))]
    [DataRow(unchecked((int)0xC0000005))]
    [DataRow(unchecked((int)0xC0000409))]
    public void IsCrashExitCode_RecognisesOnlyExpectedCrashCodes(int exitCode) =>
        Assert.IsTrue(CrashRecovery.IsCrashExitCode(exitCode));

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(unchecked((int)0xC000013A))]
    [DataRow(-1)]
    public void IsCrashExitCode_DoesNotTreatNormalOrForcedStopsAsCrashes(int exitCode) =>
        Assert.IsFalse(CrashRecovery.IsCrashExitCode(exitCode));

    [DataTestMethod]
    [DataRow(false, 0xAC71, true)]
    [DataRow(true, 0xAC71, false)]
    [DataRow(false, 0, false)]
    [DataRow(false, unchecked((int)0xC000013A), false)]
    public void ShouldRestartAfterExit_RequiresAnUncleanRecognisedCrash(bool cleanShutdown, int exitCode, bool expected) =>
        Assert.AreEqual(expected, CrashRecovery.ShouldRestartAfterExit(cleanShutdown, exitCode));

    [TestMethod]
    public void NextCrashCount_IncrementsInsideTheOneMinuteWindow()
    {
        var previous = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(3, CrashRecovery.NextCrashCount(2, previous, previous.AddSeconds(59)));
    }

    [TestMethod]
    public void NextCrashCount_ResetsOutsideTheOneMinuteWindow()
    {
        var previous = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(1, CrashRecovery.NextCrashCount(3, previous, previous.AddMinutes(1)));
    }

    [DataTestMethod]
    [DataRow(1, true)]
    [DataRow(3, true)]
    [DataRow(4, false)]
    public void AllowsRestart_StopsAfterThreeAttempts(int crashCount, bool expected) =>
        Assert.AreEqual(expected, CrashRecovery.AllowsRestart(crashCount));

    [DataTestMethod]
    [DataRow(new string[] { })]
    [DataRow(new[] { "--crash-watchdog" })]
    [DataRow(new[] { "--crash-watchdog", "not-a-pid", "event" })]
    [DataRow(new[] { "--other-mode", "1", "event" })]
    public void TryRunWatchdog_RejectsMalformedArgumentsWithoutStartingAWatcher(string[] arguments) =>
        Assert.IsFalse(CrashRecovery.TryRunWatchdog(arguments));
}
