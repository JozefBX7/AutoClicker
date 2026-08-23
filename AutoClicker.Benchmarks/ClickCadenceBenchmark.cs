// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace AutoClicker;
[CPUUsageDiagnoser]
public class ClickCadenceBenchmark
{
    private const int ClicksPerRun = 100;
    private PrecisionTimer? timer;
    [Params(0, 1, 2, 5)]
    public int PulseMilliseconds { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        timer = new PrecisionTimer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        timer?.Dispose();
    }

    [Benchmark]
    public double CadenceAt10Milliseconds()
    {
        return MeasureCadence(TimeSpan.FromMilliseconds(10));
    }

    [Benchmark]
    public double CadenceAt100Milliseconds()
    {
        return MeasureCadence(TimeSpan.FromMilliseconds(100));
    }

    private double MeasureCadence(TimeSpan interval)
    {
        var intervalTicks = interval.TotalSeconds * Stopwatch.Frequency;
        var nextClickAt = (double)Stopwatch.GetTimestamp();
        var previousClickAt = nextClickAt;
        var totalDeviation = 0d;
        for (var click = 0; click < ClicksPerRun; click++)
        {
            timer!.WaitUntil(nextClickAt);
            var clickAt = Stopwatch.GetTimestamp();
            if (click > 0)
            {
                totalDeviation += Math.Abs(clickAt - previousClickAt - intervalTicks);
            }

            if (PulseMilliseconds > 0)
            {
                timer.WaitUntil(Stopwatch.GetTimestamp() + PulseMilliseconds * Stopwatch.Frequency / 1000d);
            }

            previousClickAt = clickAt;
            nextClickAt += intervalTicks;
            if (clickAt - nextClickAt > intervalTicks)
            {
                nextClickAt = clickAt;
            }
        }

        return totalDeviation * 1000d / Stopwatch.Frequency / (ClicksPerRun - 1);
    }

    private sealed class PrecisionTimer : IDisposable
    {
        private const uint TimerAllAccess = 0x001F0003;
        private const uint CreateHighResolution = 0x00000002;
        private readonly nint handle;
        public PrecisionTimer()
        {
            handle = CreateWaitableTimerEx(nint.Zero, null, CreateHighResolution, TimerAllAccess);
            if (handle == 0)
            {
                handle = CreateWaitableTimer(nint.Zero, false, null);
            }

            if (handle == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public void WaitUntil(double targetTimestamp)
        {
            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return;
            }

            var dueTime = -Math.Max(1L, (long)Math.Ceiling(remainingTicks * 10_000_000 / Stopwatch.Frequency));
            if (!SetWaitableTimer(handle, ref dueTime, 0, nint.Zero, nint.Zero, false))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            WaitForSingleObject(handle, uint.MaxValue);
        }

        public void Dispose()
        {
            CloseHandle(handle);
        }

        [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint desiredAccess);
        [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWaitableTimer(nint attributes, bool manualReset, string? name);
        [DllImport(NativeLibraryNames.Kernel32, SetLastError = true)]
        private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completionRoutine, nint argument, bool resume);
        [DllImport(NativeLibraryNames.Kernel32)]
        private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
        [DllImport(NativeLibraryNames.Kernel32)]
        private static extern bool CloseHandle(nint handle);
    }
}
