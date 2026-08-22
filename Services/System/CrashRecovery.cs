// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AutoClicker;

/// <summary>Runs a short-lived companion process which can restart AutoClicker after a genuine crash.</summary>
internal static class CrashRecovery
{
    internal const int ManagedCrashExitCode = 0xAC71;
    internal const int MaxRestartAttemptsPerMinute = 3;
    private static readonly string SettingsPath = AppPaths.ConfigFile("rgb-settings.json");
    private static readonly string CrashHistoryPath = AppPaths.ConfigFile("crash-history.json");
    private static EventWaitHandle? cleanShutdown;
    private static bool watcherStarted;

    internal static bool TryRunWatchdog(string[] args)
    {
        if (args.Length != 3 || !string.Equals(args[0], "--crash-watchdog", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(args[1], out var parentProcessId)) return false;

        RunWatchdog(parentProcessId, args[2]);
        return true;
    }

    internal static void StartIfEnabled()
    {
        // The watcher is another AutoClicker process running in watchdog mode.
        if (watcherStarted || !IsEnabled()) return;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;

        var eventName = $"Local\\AutoClicker.CleanExit.{Environment.ProcessId}.{Guid.NewGuid():N}";
        try
        {
            cleanShutdown = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            var watcher = Process.Start(new ProcessStartInfo(executable, $"--crash-watchdog {Environment.ProcessId} {eventName}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            });
            watcherStarted = watcher is not null;
            if (watcherStarted)
            {
                try { watcher!.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                AppLog.Info("Crash recovery watchdog started in idle priority mode.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not start crash recovery watchdog", exception);
            cleanShutdown?.Dispose();
            cleanShutdown = null;
        }
    }

    internal static void UpdateEnabled(bool enabled)
    {
        if (enabled) StartIfEnabled();
        else MarkCleanShutdown();
    }

    internal static void MarkCleanShutdown()
    {
        try { cleanShutdown?.Set(); }
        catch (ObjectDisposedException) { }
        finally
        {
            cleanShutdown?.Dispose();
            cleanShutdown = null;
            watcherStarted = false;
        }
    }

    internal static void ExitAfterCrash() => Environment.Exit(ManagedCrashExitCode);

    private static void RunWatchdog(int parentProcessId, string cleanExitEventName)
    {
        try
        {
            // Either signal means the main process no longer needs watching.
            using var parent = Process.GetProcessById(parentProcessId);
            using var cleanExit = EventWaitHandle.OpenExisting(cleanExitEventName);
            using var parentExited = new ManualResetEvent(false);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => parentExited.Set();
            if (parent.HasExited) parentExited.Set();

            // Kernel wait: no polling while AutoClicker is healthy.
            var signalled = WaitHandle.WaitAny([cleanExit, parentExited]);
            // Clean exits are ignored; only recognised crash codes may restart.
            if (!ShouldRestartAfterExit(signalled == 0 || cleanExit.WaitOne(0), parent.ExitCode)) return;
            if (!RecordCrashAndAllowRestart())
            {
                AppLog.Info("Crash recovery paused after repeated crashes; AutoClicker was not restarted.");
                return;
            }

            AppLog.Info($"Unexpected exit detected (code 0x{parent.ExitCode:X8}); restarting AutoClicker.");
            Thread.Sleep(350);
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
        }
        catch (Exception exception)
        {
            AppLog.Error("Crash recovery watchdog failed", exception);
        }
    }

    private static bool IsEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            return JsonSerializer.Deserialize<RgbSettings>(File.ReadAllText(SettingsPath))?.CrashRecoveryEnabled ?? true;
        }
        catch { return true; }
    }

    internal static bool IsCrashExitCode(int exitCode) => unchecked((uint)exitCode) switch
    {
        ManagedCrashExitCode => true,
        0xE0434352 or 0x80131506 or 0xC0000005 or 0xC0000409 => true,
        _ => false
    };

    internal static bool ShouldRestartAfterExit(bool cleanShutdownSignalled, int exitCode) =>
        !cleanShutdownSignalled && IsCrashExitCode(exitCode);

    private static bool RecordCrashAndAllowRestart()
    {
        try
        {
            // Keep restart limits across process restarts.
            var now = DateTimeOffset.UtcNow;
            var history = File.Exists(CrashHistoryPath)
                ? JsonSerializer.Deserialize<CrashHistory>(File.ReadAllText(CrashHistoryPath)) ?? new CrashHistory()
                : new CrashHistory();
            history.Count = NextCrashCount(history.Count, history.LastCrashUtc, now);
            history.LastCrashUtc = now;
            Directory.CreateDirectory(Path.GetDirectoryName(CrashHistoryPath)!);
            File.WriteAllText(CrashHistoryPath, JsonSerializer.Serialize(history));
            return AllowsRestart(history.Count);
        }
        catch { return true; }
    }

    internal static int NextCrashCount(int previousCount, DateTimeOffset previousCrash, DateTimeOffset now) =>
        now - previousCrash < TimeSpan.FromMinutes(1) ? previousCount + 1 : 1;

    internal static bool AllowsRestart(int crashCount) => crashCount <= MaxRestartAttemptsPerMinute;

    private sealed class CrashHistory
    {
        public DateTimeOffset LastCrashUtc { get; set; }
        public int Count { get; set; }
    }
}
