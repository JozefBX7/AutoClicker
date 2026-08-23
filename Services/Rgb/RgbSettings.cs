// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using OpenRGB.NET;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;

namespace AutoClicker;

public sealed class RgbSettings
{
    public bool Enabled { get; set; }
    public int DeviceIndex { get; set; } = -1;
    public string DeviceName { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool StopAutoStartedOnExit { get; set; } = true;
    public string IdleProfileName { get; set; } = string.Empty;
    public string IndicatorColor { get; set; } = "#22D3EE";
    public string LightingEffect { get; set; } = RgbLightingEffectIds.Constant;
    [JsonPropertyName(RgbSettingJsonNames.LegacyEffectSpeedMilliseconds)]
    public int EffectSpeedMilliseconds { get; set; } = 450;

    // "Pulse" was the former name for the on/off effect. Keep it as a
    // backwards-compatible alias so existing saved settings become Blink.
    public bool UsesBlinkEffect => string.Equals(LightingEffect, RgbLightingEffectIds.Blink, StringComparison.OrdinalIgnoreCase)
        || string.Equals(LightingEffect, RgbLightingEffectIds.LegacyBlink, StringComparison.OrdinalIgnoreCase);
    public bool UsesFadeEffect => string.Equals(LightingEffect, RgbLightingEffectIds.Fade, StringComparison.OrdinalIgnoreCase);

    public RgbSettings Clone() => new()
    {
        Enabled = Enabled,
        DeviceIndex = DeviceIndex,
        DeviceName = DeviceName,
        AutoStart = AutoStart,
        StopAutoStartedOnExit = StopAutoStartedOnExit,
        IdleProfileName = IdleProfileName,
        IndicatorColor = IndicatorColor,
        LightingEffect = LightingEffect,
        EffectSpeedMilliseconds = EffectSpeedMilliseconds
    };
}

public sealed record KeyboardDevice(int Index, string Name)
{
    public override string ToString() => Name;
}

public static class OpenRgbHighlighter
{
    private const string OpenRgbClientName = AppIdentity.Name;
    private const string OpenRgbReadinessClientName = AppIdentity.Name + " readiness probe";
    private const int SdkProbeTimeoutMilliseconds = 600;
    private const int SdkStartupPollDelayMilliseconds = 250;
    private const int AutoStartedSdkReadyTimeoutMilliseconds = 15_000;
    private const int ExistingProcessStartupGraceMilliseconds = 10_000;
    internal const int FadeFramesPerCycle = 12;
    internal const int MaximumFadeFramesPerCycle = 36;
    internal const int FadeTargetFrameDurationMilliseconds = 100;
    internal const int MinimumFadeCycleMilliseconds = 600;
    internal const int MaximumFadeCycleMilliseconds = 3500;
    internal const int SolidPreviewDurationMilliseconds = 5000;
    // Never stop an OpenRGB instance we did not start.
    private static readonly object StartedProcessLock = new();
    private static readonly SemaphoreSlim SdkStartupLock = new(1, 1);
    private static readonly object IndicatorWriteLock = new();
    private static readonly Dictionary<int, IndicatorDeviceState> ActiveIndicators = [];
    private static Process? processStartedByAutoClicker;
    private static bool autoStartSuppressed;

    internal static bool ShouldStartOnApplicationLaunch(RgbSettings settings) => settings.Enabled && settings.AutoStart;

    public static async Task<OpenRgbAvailability> EnsureSdkAsync(RgbSettings settings)
    {
        if (await IsSdkAvailableAsync()) return new(true, null);
        if (!settings.AutoStart)
            return new(false, "OpenRGB's SDK server is not available. Enable it in OpenRGB, or turn on automatic startup here.");
        if (IsAutoStartSuppressed())
            return new(false, "OpenRGB was not started because AutoClicker is shutting down.");

        await SdkStartupLock.WaitAsync();
        try
        {
            // Another request may have started OpenRGB while this caller was waiting.
            if (await IsSdkAvailableAsync()) return new(true, null);
            if (IsAutoStartSuppressed())
                return new(false, "OpenRGB was not started because AutoClicker is shutting down.");

            var runningProcesses = Process.GetProcessesByName("OpenRGB");
            try
            {
                if (runningProcesses.Length > 0)
                {
                    var gracePeriod = RemainingStartupGracePeriod(runningProcesses);
                    if (gracePeriod > TimeSpan.Zero && await WaitForSdkAsync(gracePeriod))
                        return new(true, "OpenRGB's SDK server became available.");

                    return new(false, "OpenRGB is already running, but its SDK server is not available. Enable its SDK server on port 6742, then refresh.");
                }
            }
            finally
            {
                foreach (var runningProcess in runningProcesses) runningProcess.Dispose();
            }

            var executable = FindOpenRgbExecutable();
            if (executable is null)
                return new(false, "OpenRGB is not installed in its usual location. Install OpenRGB, start it once, then refresh.");

            try
            {
                Process process;
                lock (StartedProcessLock)
                {
                    // Coordinate with shutdown so a process can never be launched
                    // after the final owner-only stop has taken its snapshot.
                    if (autoStartSuppressed)
                        return new(false, "OpenRGB was not started because AutoClicker is shutting down.");
                    process = Process.Start(CreateServerStartInfo(executable))
                        ?? throw new InvalidOperationException("OpenRGB could not be started.");
                    processStartedByAutoClicker = process;
                }
                AppLog.Info($"Started OpenRGB SDK server process | PID={process.Id} | Path={executable} | Host=127.0.0.1 | Port=6742");

                if (await WaitForSdkAsync(TimeSpan.FromMilliseconds(AutoStartedSdkReadyTimeoutMilliseconds), process))
                    return new(true, "OpenRGB was started automatically.", WasStarted: true);

                if (HasExited(process))
                    return new(false, $"OpenRGB exited before its SDK server became available{ExitCodeSuffix(process)}.");
            }
            catch (Exception exception)
            {
                return new(false, OpenRgbMessages.CouldNotStart(exception.Message));
            }
            return new(false, "OpenRGB started, but its SDK server did not become available. Open OpenRGB and enable its SDK server.");
        }
        finally
        {
            SdkStartupLock.Release();
        }
    }

    public static void StopAutoStartedServer()
    {
        Process? process;
        lock (StartedProcessLock)
        {
            process = processStartedByAutoClicker;
            processStartedByAutoClicker = null;
        }
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                AppLog.Info($"Stopping OpenRGB process started by AutoClicker | PID={process.Id}");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not stop the OpenRGB process started by AutoClicker", exception);
        }
        finally { process.Dispose(); }
    }

    internal static void SuppressAutoStart()
    {
        lock (StartedProcessLock) autoStartSuppressed = true;
    }

    private static bool IsAutoStartSuppressed()
    {
        lock (StartedProcessLock) return autoStartSuppressed;
    }

    public static string[] GetProfiles()
    {
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        return client.GetProfiles()
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool TryLoadProfile(string? profileName, out string? error)
    {
        error = null;
        var name = (profileName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            error = "Choose an OpenRGB profile first.";
            return false;
        }
        try
        {
            lock (IndicatorWriteLock)
            {
                using var client = new OpenRgbClient(name: OpenRgbClientName);
                client.LoadProfile(name);
                ActiveIndicators.Clear();
            }
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error($"OpenRGB profile load failed: {name}", exception);
            error = $"OpenRGB could not load profile '{name}': {exception.Message}";
            return false;
        }
    }

    private static Task<bool> IsSdkAvailableAsync() => Task.Run(() =>
    {
        try
        {
            // A TCP listener alone does not prove this is a ready OpenRGB SDK server.
            // Complete the protocol handshake and one small request before callers use it.
            using var client = new OpenRgbClient(name: OpenRgbReadinessClientName, timeoutMs: SdkProbeTimeoutMilliseconds);
            _ = client.GetControllerCount();
            return true;
        }
        catch { return false; }
    });

    private static async Task<bool> WaitForSdkAsync(TimeSpan timeout, Process? process = null)
    {
        var deadline = Stopwatch.GetTimestamp() + timeout.TotalSeconds * Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (process is not null && HasExited(process)) return false;
            if (await IsSdkAvailableAsync()) return true;
            await Task.Delay(SdkStartupPollDelayMilliseconds);
        }
        return await IsSdkAvailableAsync();
    }

    private static TimeSpan RemainingStartupGracePeriod(IEnumerable<Process> processes)
    {
        var now = DateTime.Now;
        var remaining = TimeSpan.Zero;
        foreach (var process in processes)
        {
            try
            {
                var age = now - process.StartTime;
                var candidate = TimeSpan.FromMilliseconds(ExistingProcessStartupGraceMilliseconds) - age;
                if (candidate > remaining) remaining = candidate;
            }
            catch
            {
                // If process metadata is inaccessible, keep the no-second-instance
                // safeguard and report the unavailable server without delaying.
            }
        }
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    private static string ExitCodeSuffix(Process process)
    {
        try { return $" (exit code {process.ExitCode})"; }
        catch { return string.Empty; }
    }

    internal static ProcessStartInfo CreateServerStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        startInfo.ArgumentList.Add("--server");
        startInfo.ArgumentList.Add("--server-host");
        startInfo.ArgumentList.Add("127.0.0.1");
        return startInfo;
    }

    internal static RgbSettings CreateIdleProfileSettings(RgbSettings source, bool allowAutoStart)
    {
        var settings = source.Clone();
        settings.Enabled = true;
        settings.AutoStart = allowAutoStart && source.AutoStart;
        return settings;
    }

    private static string? FindOpenRgbExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static KeyboardDevice? ResolveKeyboard(RgbSettings settings)
    {
        return SelectKeyboard(FindKeyboards(), settings);
    }

    internal static KeyboardDevice? SelectKeyboard(IEnumerable<KeyboardDevice> keyboards, RgbSettings settings)
    {
        var candidates = keyboards.ToArray();
        // Indices can move after reconnecting hardware.
        var namedMatch = candidates.FirstOrDefault(device => string.Equals(device.Name, settings.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (namedMatch is not null) return namedMatch;
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static bool TryNormalizeIndicatorColor(string? value, out string normalized)
    {
        var hex = (value ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length == 6
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            normalized = $"#{hex.ToUpperInvariant()}";
            return true;
        }
        normalized = "#22D3EE";
        return false;
    }

    public static KeyboardDevice[] FindKeyboards()
    {
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        return client.GetAllControllerData()
            .Where(OpenRgbDeviceClassifier.IsKeyboard)
            .Select(device => new KeyboardDevice(device.Index, device.Name))
            .ToArray();
    }

    public static RgbLightingSnapshot? EnableKeyIndicator(RgbSettings settings, string keyName, out string? error, bool lightImmediately = true)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: OpenRgbClientName);
            var keyboard = client.GetControllerData(settings.DeviceIndex);

            var ledIndex = FindLedIndex(keyboard, keyName);
            if (ledIndex is null) { error = $"OpenRGB could not light {keyName} on {keyboard.Name}. Choose a standard keyboard key, then try again."; return null; }
            if (keyboard.Colors.Length != keyboard.Leds.Length) { error = "This keyboard does not expose per-key colours to OpenRGB."; return null; }

            // Restore the original keyboard colours when finished.
            var snapshot = new RgbLightingSnapshot(keyboard.Index, keyboard.Colors.ToArray(), CaptureMode(keyboard), ledIndex.Value, IndicatorColor(settings));
            if (lightImmediately) LightIndicator(snapshot);
            return snapshot;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB indicator update failed", exception);
            error = OpenRgbMessages.Unavailable(exception.Message);
            return null;
        }
    }

    public static bool CanHighlightKey(RgbSettings settings, string keyName, out string? error)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: OpenRgbClientName);
            var keyboard = client.GetControllerData(settings.DeviceIndex);
            if (FindLedIndex(keyboard, keyName) is null)
            {
                error = $"OpenRGB cannot map {keyName} on {keyboard.Name}. Choose a standard keyboard key to light it.";
                return false;
            }
            if (keyboard.Colors.Length != keyboard.Leds.Length)
            {
                error = "This keyboard does not expose per-key colours to OpenRGB.";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = OpenRgbMessages.Unavailable(exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Re-sends the keyboard's complete current colour buffer to clear any
    /// per-key highlight that was left behind by a transient direct LED update.
    /// </summary>
    public static string? ClearStuckKeyboardLighting(RgbSettings settings)
    {
        try
        {
            using var client = new OpenRgbClient(name: OpenRgbClientName);
            var keyboard = client.GetControllerData(settings.DeviceIndex);
            if (keyboard.Colors.Length == 0)
                return $"{keyboard.Name} does not expose colours that OpenRGB can refresh.";

            client.SetCustomMode(keyboard.Index);
            client.UpdateLeds(keyboard.Index, CreateRecoveryColors(keyboard.Colors));

            AppLog.Info($"Refreshed keyboard colours to clear stuck OpenRGB lighting | Device={keyboard.Name}");
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB stuck lighting recovery failed", exception);
            return $"OpenRGB could not clear the keyboard lighting: {exception.Message}";
        }
    }

    internal static Color[] CreateRecoveryColors(IReadOnlyCollection<Color> currentColors) => currentColors.ToArray();

    public static void RestoreIndicator(RgbLightingSnapshot snapshot)
    {
        try
        {
            lock (IndicatorWriteLock)
            {
                ClearIndicatorCore(snapshot);
            }
        }
        catch (Exception exception) { AppLog.Error("OpenRGB indicator restore failed", exception); }
    }

    public static void ClearIndicator(RgbLightingSnapshot snapshot)
    {
        RestoreIndicator(snapshot);
    }

    public static async Task BlinkIndicatorAsync(RgbLightingSnapshot snapshot, int halfCycleMilliseconds, CancellationToken cancellation)
    {
        var halfCycle = Math.Clamp(halfCycleMilliseconds, 120, 2000);
        var lit = true;
        try
        {
            while (true)
            {
                await Task.Delay(halfCycle, cancellation);
                if (lit) ClearIndicatorFrame(snapshot); else LightIndicator(snapshot);
                lit = !lit;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("OpenRGB blink effect failed", exception); }
    }

    public static async Task FadeIndicatorAsync(RgbLightingSnapshot snapshot, int cycleMilliseconds, CancellationToken cancellation)
    {
        var cycle = Math.Clamp(cycleMilliseconds, MinimumFadeCycleMilliseconds, MaximumFadeCycleMilliseconds);
        var framesPerCycle = GetFadeFramesPerCycle(cycle);
        var frameDelay = TimeSpan.FromMilliseconds(cycle / framesPerCycle);
        try
        {
            for (var frame = 0; ; frame = (frame + 1) % framesPerCycle)
            {
                var strength = 0.5d - 0.5d * Math.Cos(2d * Math.PI * frame / framesPerCycle);
                SetIndicatorBlend(snapshot, strength);
                await Task.Delay(frameDelay, cancellation);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("OpenRGB fade pulse effect failed", exception); }
    }

    internal static int GetFadeFramesPerCycle(int cycleMilliseconds)
    {
        var targetFrames = (int)Math.Ceiling(cycleMilliseconds / (double)FadeTargetFrameDurationMilliseconds);
        return Math.Clamp(targetFrames, FadeFramesPerCycle, MaximumFadeFramesPerCycle);
    }

    public static async Task<string?> FlashKeyAsync(RgbSettings settings, string keyName)
    {
        var snapshot = EnableKeyIndicator(settings, keyName, out var error);
        if (snapshot is null) return error ?? "OpenRGB could not start the lighting test.";

        try
        {
            for (var flash = 0; flash < 3; flash++)
            {
                if (flash > 0) LightIndicator(snapshot);
                await Task.Delay(170);
                RestoreIndicator(snapshot);
                if (flash < 2) await Task.Delay(110);
            }
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB lighting test failed", exception);
            return $"OpenRGB test failed: {exception.Message}";
        }
        finally
        {
            // Every exit path restores the pre-test LED state.
            RestoreIndicator(snapshot);
        }
    }

    // The global Settings test validates the selected keyboard itself, independent of key mapping.
    public static async Task<string?> FlashKeyboardAsync(RgbSettings settings)
    {
        var snapshot = EnableKeyboardIndicator(settings, out var error);
        if (snapshot is null) return error ?? "OpenRGB could not start the keyboard lighting test.";

        try
        {
            for (var flash = 0; flash < 3; flash++)
            {
                LightKeyboard(snapshot);
                await Task.Delay(170);
                RestoreKeyboard(snapshot);
                if (flash < 2) await Task.Delay(110);
            }
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB keyboard lighting test failed", exception);
            return $"OpenRGB test failed: {exception.Message}";
        }
        finally
        {
            // Preserve the selected keyboard's pre-test colours even if its SDK call fails mid-test.
            RestoreKeyboard(snapshot);
        }
    }

    public static RgbKeyboardSnapshot? EnableKeyboardIndicator(RgbSettings settings, out string? error, bool lightImmediately = true)
    {
        var snapshot = CaptureKeyboardForTest(settings, out error);
        if (snapshot is not null && lightImmediately) LightKeyboard(snapshot);
        return snapshot;
    }

    public static async Task BlinkKeyboardAsync(RgbKeyboardSnapshot snapshot, int halfCycleMilliseconds, CancellationToken cancellation)
    {
        var halfCycle = Math.Clamp(halfCycleMilliseconds, 120, 2000);
        var lit = true;
        try
        {
            while (true)
            {
                await Task.Delay(halfCycle, cancellation);
                if (lit) RestoreKeyboard(snapshot); else LightKeyboard(snapshot);
                lit = !lit;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("OpenRGB keyboard blink effect failed", exception); }
    }

    public static async Task FadeKeyboardAsync(RgbKeyboardSnapshot snapshot, int cycleMilliseconds, CancellationToken cancellation)
    {
        var cycle = Math.Clamp(cycleMilliseconds, MinimumFadeCycleMilliseconds, MaximumFadeCycleMilliseconds);
        var framesPerCycle = GetFadeFramesPerCycle(cycle);
        var frameDelay = TimeSpan.FromMilliseconds(cycle / framesPerCycle);
        try
        {
            for (var frame = 0; ; frame = (frame + 1) % framesPerCycle)
            {
                var strength = 0.5d - 0.5d * Math.Cos(2d * Math.PI * frame / framesPerCycle);
                SetKeyboardBlend(snapshot, strength);
                await Task.Delay(frameDelay, cancellation);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("OpenRGB keyboard fade effect failed", exception); }
    }

    public static async Task<string?> ShowKeySolidAsync(RgbSettings settings, string keyName, CancellationToken cancellation = default)
    {
        var snapshot = EnableKeyIndicator(settings, keyName, out var error, lightImmediately: false);
        if (snapshot is null) return error ?? "OpenRGB could not start the lighting test.";

        try
        {
            LightIndicator(snapshot);
            await Task.Delay(SolidPreviewDurationMilliseconds, cancellation);
            return null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB lighting test failed", exception);
            return $"OpenRGB test failed: {exception.Message}";
        }
        finally
        {
            // Every exit path restores the pre-test LED state.
            RestoreIndicator(snapshot);
        }
    }

    public static void LightIndicator(RgbLightingSnapshot snapshot)
    {
        lock (IndicatorWriteLock) WriteKey(snapshot, snapshot.IndicatorColor);
    }

    private static void WriteKey(RgbLightingSnapshot snapshot, Color color)
    {
        SetIndicatorColor(snapshot, color);
    }

    private static RgbKeyboardSnapshot? CaptureKeyboardForTest(RgbSettings settings, out string? error)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: OpenRgbClientName);
            var keyboard = client.GetControllerData(settings.DeviceIndex);
            if (keyboard.Colors.Length == 0)
            {
                error = $"{keyboard.Name} does not expose colours that OpenRGB can test.";
                return null;
            }
            return new RgbKeyboardSnapshot(keyboard.Index, keyboard.Colors.ToArray(), CaptureMode(keyboard), IndicatorColor(settings));
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB keyboard test setup failed", exception);
            error = OpenRgbMessages.Unavailable(exception.Message);
            return null;
        }
    }

    private static void LightKeyboard(RgbKeyboardSnapshot snapshot)
    {
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateLeds(snapshot.DeviceIndex, CreateKeyboardFlashColors(snapshot.Colors, snapshot.IndicatorColor));
    }

    public static void RestoreKeyboard(RgbKeyboardSnapshot snapshot)
    {
        try
        {
            using var client = new OpenRgbClient(name: OpenRgbClientName);
            client.SetCustomMode(snapshot.DeviceIndex);
            client.UpdateLeds(snapshot.DeviceIndex, snapshot.Colors);
            RestoreMode(client, snapshot.DeviceIndex, snapshot.Mode);
        }
        catch (Exception exception) { AppLog.Error("OpenRGB keyboard test restore failed", exception); }
    }

    private static void RestoreKey(RgbLightingSnapshot snapshot)
    {
        lock (IndicatorWriteLock) WriteKey(snapshot, snapshot.Colors[snapshot.KeyIndex]);
    }

    private static void ClearKey(RgbLightingSnapshot snapshot)
    {
        lock (IndicatorWriteLock) WriteKey(snapshot, snapshot.Colors[snapshot.KeyIndex]);
    }

    private static void ClearIndicatorFrame(RgbLightingSnapshot snapshot)
    {
        lock (IndicatorWriteLock) WriteKey(snapshot, new Color(0, 0, 0));
    }

    private static void SetIndicatorBlend(RgbLightingSnapshot snapshot, double strength)
    {
        lock (IndicatorWriteLock) WriteKey(snapshot, BlendColor(new Color(0, 0, 0), snapshot.IndicatorColor, strength));
    }

    private static void SetIndicatorColor(RgbLightingSnapshot snapshot, Color color)
    {
        if (!ActiveIndicators.TryGetValue(snapshot.DeviceIndex, out var state))
        {
            state = new IndicatorDeviceState(snapshot.Colors.ToArray(), snapshot.Mode);
            ActiveIndicators.Add(snapshot.DeviceIndex, state);
        }
        state.IndicatorIds.Add(snapshot.Id);
        state.Colors[snapshot.KeyIndex] = color;
        PublishIndicators(snapshot.DeviceIndex);
    }

    private static void ClearIndicatorCore(RgbLightingSnapshot snapshot)
    {
        if (!ActiveIndicators.TryGetValue(snapshot.DeviceIndex, out var state)) return;
        RestoreIndicatorColor(state.Colors, snapshot).CopyTo(state.Colors, 0);
        state.IndicatorIds.Remove(snapshot.Id);
        if (state.IndicatorIds.Count > 0)
        {
            PublishIndicators(snapshot.DeviceIndex);
            return;
        }

        ActiveIndicators.Remove(snapshot.DeviceIndex);
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateLeds(snapshot.DeviceIndex, state.Colors);
        RestoreMode(client, snapshot.DeviceIndex, state.Mode);
    }

    internal static Color[] RestoreIndicatorColor(IReadOnlyCollection<Color> currentColors, RgbLightingSnapshot snapshot)
    {
        var restored = currentColors.ToArray();
        restored[snapshot.KeyIndex] = snapshot.Colors[snapshot.KeyIndex];
        return restored;
    }

    private static void PublishIndicators(int deviceIndex)
    {
        if (!ActiveIndicators.TryGetValue(deviceIndex, out var state)) return;
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        client.SetCustomMode(deviceIndex);
        client.UpdateLeds(deviceIndex, state.Colors);
    }

    private static RgbDeviceModeSnapshot CaptureMode(Device keyboard)
    {
        var mode = keyboard.ActiveMode;
        return new RgbDeviceModeSnapshot(keyboard.ActiveModeIndex, mode.SupportsSpeed ? mode.Speed : null, mode.SupportsDirection ? mode.Direction : null, mode.Colors.ToArray());
    }

    private static void RestoreMode(OpenRgbClient client, int deviceIndex, RgbDeviceModeSnapshot mode) =>
        client.UpdateMode(deviceIndex, mode.Index, mode.Speed, mode.Direction, mode.Colors);

    private sealed class IndicatorDeviceState(Color[] colors, RgbDeviceModeSnapshot mode)
    {
        public Color[] Colors { get; } = colors;
        public RgbDeviceModeSnapshot Mode { get; } = mode;
        public HashSet<Guid> IndicatorIds { get; } = [];
    }

    internal static Color BlendColor(Color baseColor, Color indicatorColor, double strength)
    {
        var amount = Math.Clamp(strength, 0d, 1d);
        return new Color(
            (byte)Math.Round(baseColor.R + (indicatorColor.R - baseColor.R) * amount),
            (byte)Math.Round(baseColor.G + (indicatorColor.G - baseColor.G) * amount),
            (byte)Math.Round(baseColor.B + (indicatorColor.B - baseColor.B) * amount));
    }

    private static void SetKeyboardBlend(RgbKeyboardSnapshot snapshot, double strength)
    {
        using var client = new OpenRgbClient(name: OpenRgbClientName);
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateLeds(snapshot.DeviceIndex, CreateKeyboardBlendColors(snapshot.Colors, snapshot.IndicatorColor, strength));
    }

    internal static Color[] CreateKeyboardFlashColors(IReadOnlyCollection<Color> currentColors, Color indicatorColor) => Enumerable.Repeat(indicatorColor, currentColors.Count).ToArray();

    internal static Color[] CreateKeyboardBlendColors(IEnumerable<Color> currentColors, Color indicatorColor, double strength) => currentColors.Select(color => BlendColor(color, indicatorColor, strength)).ToArray();

    private static Color IndicatorColor(RgbSettings settings)
    {
        TryNormalizeIndicatorColor(settings.IndicatorColor, out var hex);
        return ColorFromNormalizedHex(hex);
    }

    private static Color ColorFromNormalizedHex(string hex) =>
        new(
            byte.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static bool SameKey(string ledName, string keyName)
    {
        static string Normalize(string value) => value.Replace("KEY_", "", StringComparison.OrdinalIgnoreCase).Replace("KEY:", "", StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
        var led = Normalize(ledName);
        return KeyAliases(keyName).Select(Normalize).Any(alias => alias == led);
    }

    private static int? FindLedIndex(Device keyboard, string keyName)
    {
        static string Normalize(string value)
        {
            var cleaned = value.Replace("KEY_", "", StringComparison.OrdinalIgnoreCase)
                .Replace("KEY:", "", StringComparison.OrdinalIgnoreCase)
                .Replace("+", " PLUS ", StringComparison.Ordinal)
                .Replace("-", " MINUS ", StringComparison.Ordinal)
                .Replace("/", " SLASH ", StringComparison.Ordinal)
                .Replace("*", " STAR ", StringComparison.Ordinal)
                .Replace(".", " DOT ", StringComparison.Ordinal)
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty);
            return new string(cleaned.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        var aliases = KeyAliases(keyName).Select(Normalize).Distinct(StringComparer.Ordinal).ToArray();
        for (var aliasIndex = 0; aliasIndex < aliases.Length; aliasIndex++)
        {
            var alias = aliases[aliasIndex];
            var bestIndex = -1;
            var bestScore = int.MinValue;
            for (var index = 0; index < keyboard.Leds.Length; index++)
            {
                var ledTokens = LedNameAliases(keyboard.Leds[index].Name).Select(Normalize).Distinct(StringComparer.Ordinal);
                if (!ledTokens.Contains(alias, StringComparer.Ordinal)) continue;

                var score = NumpadHintScore(keyboard.Leds[index].Name)
                    + ((aliases.Length - aliasIndex) * 100)
                    + alias.Length;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            if (bestIndex >= 0) return bestIndex;
        }

        return null;
    }

    private static IEnumerable<string> LedNameAliases(string ledName)
    {
        yield return ledName;
        var parts = ledName.Split(['/', '\\', '|', '-', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            yield return part;

            var containsKeypadHint = part.Contains("keypad", StringComparison.OrdinalIgnoreCase)
                || part.Contains("numpad", StringComparison.OrdinalIgnoreCase)
                || part.Contains("num", StringComparison.OrdinalIgnoreCase)
                || part.Contains("kp", StringComparison.OrdinalIgnoreCase);
            if (!containsKeypadHint || !part.Contains("And", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var split in part.Split("And", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!string.IsNullOrWhiteSpace(split))
                    yield return split;
        }
    }

    private static int NumpadHintScore(string ledName)
    {
        var value = ledName.Replace("_", " ", StringComparison.Ordinal).Trim();
        var score = 0;
        if (value.Contains("numpad", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (value.Contains("keypad", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (value.Contains("num ", StringComparison.OrdinalIgnoreCase)) score += 3;
        if (value.Contains("kp", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static IEnumerable<string> KeyAliases(string keyName)
    {
        yield return keyName;
        var normalized = keyName.Replace(" ", string.Empty);
        foreach (var alias in normalized.ToUpperInvariant() switch
        {
            "LWIN" or "LEFTWIN" => new[] { "Left Windows", "LWin", "Left GUI", "LGUI", "Left Meta", "Left Super" },
            "RWIN" or "RIGHTWIN" => new[] { "Right Windows", "RWin", "Right GUI", "RGUI", "Right Meta", "Right Super" },
            "LEFTCTRL" or "LEFTCONTROL" => new[] { "Left Ctrl", "L Ctrl", "LCtrl", "Left Control" },
            "RIGHTCTRL" or "RIGHTCONTROL" => new[] { "Right Ctrl", "R Ctrl", "RCtrl", "Right Control" },
            "LEFTSHIFT" => new[] { "Left Shift", "L Shift", "LShift" },
            "RIGHTSHIFT" => new[] { "Right Shift", "R Shift", "RShift" },
            "LEFTALT" => new[] { "Left Alt", "L Alt", "LAlt", "Alt Left" },
            "RIGHTALT" => new[] { "Right Alt", "R Alt", "RAlt", "Alt Gr", "AltGr" },
            "RETURN" => new[] { "Enter", "Return", "Number Pad Enter", "Num Enter", "Numpad Enter", "NumpadEnter", "Keypad Enter", "KeypadEnter", "KP Enter", "KPEnter" },
            "ESCAPE" => new[] { "Esc", "Escape" },
            "BACK" => new[] { "Backspace", "Back" },
            "TAB" => new[] { "Tab" },
            "CAPITAL" => new[] { "Caps Lock", "CapsLock", "Caps" },
            "INSERT" => new[] { "Insert", "Ins" },
            "DELETE" => new[] { "Delete", "Del" },
            "HOME" => new[] { "Home" },
            "END" => new[] { "End" },
            "LEFT" => new[] { "Left", "Left Arrow" },
            "RIGHT" => new[] { "Right", "Right Arrow" },
            "UP" => new[] { "Up", "Up Arrow" },
            "DOWN" => new[] { "Down", "Down Arrow" },
            "PRIOR" => new[] { "Page Up", "PageUp", "PgUp" },
            "NEXT" => new[] { "Page Down", "PageDown", "PgDn" },
            "SNAPSHOT" => new[] { "Print Screen", "PrintScreen", "PrtSc", "PrtScn" },
            "SCROLL" => new[] { "Scroll Lock", "ScrollLock", "ScrLk" },
            "PAUSE" => new[] { "Pause", "Pause Break", "Pause/Break", "Break" },
            "NUMLOCK" => new[] { "Num Lock", "NumLock" },
            "DIVIDE" => new[] { "Number Pad /", "Num /", "Numpad /", "Keypad /", "KP /" },
            "MULTIPLY" => new[] { "Number Pad *", "Num *", "Numpad *", "Keypad *", "KP *" },
            "SUBTRACT" => new[] { "Number Pad -", "Num -", "Numpad -", "Keypad -", "KP -" },
            "ADD" => new[] { "Number Pad +", "Num +", "Numpad +", "Keypad +", "KP +" },
            "DECIMAL" => new[] { "Number Pad .", "Num .", "Numpad .", "Keypad .", "KP ." },
            "NUMPAD0" => new[] { "Number Pad 0", "Num 0", "Numpad 0", "Keypad 0" },
            "NUMPAD1" => new[] { "Number Pad 1", "Num 1", "Numpad 1", "Keypad 1" },
            "NUMPAD2" => new[] { "Number Pad 2", "Num 2", "Numpad 2", "Keypad 2" },
            "NUMPAD3" => new[] { "Number Pad 3", "Num 3", "Numpad 3", "Keypad 3" },
            "NUMPAD4" => new[] { "Number Pad 4", "Num 4", "Numpad 4", "Keypad 4" },
            "NUMPAD5" => new[] { "Number Pad 5", "Num 5", "Numpad 5", "Keypad 5" },
            "NUMPAD6" => new[] { "Number Pad 6", "Num 6", "Numpad 6", "Keypad 6" },
            "NUMPAD7" => new[] { "Number Pad 7", "Num 7", "Numpad 7", "Keypad 7" },
            "NUMPAD8" => new[] { "Number Pad 8", "Num 8", "Numpad 8", "Keypad 8" },
            "NUMPAD9" => new[] { "Number Pad 9", "Num 9", "Numpad 9", "Keypad 9" },
            "D0" => new[] { "0" },
            "D1" => new[] { "1" },
            "D2" => new[] { "2" },
            "D3" => new[] { "3" },
            "D4" => new[] { "4" },
            "D5" => new[] { "5" },
            "D6" => new[] { "6" },
            "D7" => new[] { "7" },
            "D8" => new[] { "8" },
            "D9" => new[] { "9" },
            "OEMTILDE" => new[] { "Grave", "Backtick", "Tilde", "`" },
            "OEMMINUS" => new[] { "Minus", "Hyphen", "-" },
            "OEMPLUS" => new[] { "Equal", "Equals", "+" },
            "OEMOPENBRACKETS" => new[] { "Left Bracket", "[" },
            "OEMCLOSEBRACKETS" => new[] { "Right Bracket", "]" },
            "OEMPIPE" => new[] { "Backslash", "Pipe", "\\" },
            "OEMSEMICOLON" => new[] { "Semicolon", ";" },
            "OEMQUOTES" => new[] { "Apostrophe", "Quote", "'" },
            "OEMCOMMA" => new[] { "Comma", "," },
            "OEMPERIOD" => new[] { "Period", "Dot", "." },
            "OEMQUESTION" => new[] { "Slash", "Question", "/" },
            "MEDIASTOP" => new[] { "Media Stop", "Stop", "MediaStop" },
            "MEDIAPLAYPAUSE" => new[] { "Play Pause", "Media Play", "Play/Pause" },
            "MEDIANEXTTRACK" => new[] { "Next Track", "Media Next", "Next" },
            "MEDIAPREVIOUSTRACK" => new[] { "Previous Track", "Media Previous", "Previous" },
            "VOLUMEMUTE" => new[] { "Mute", "Volume Mute" },
            "VOLUMEDOWN" => new[] { "Volume Down", "Vol Down" },
            "VOLUMEUP" => new[] { "Volume Up", "Vol Up" },
            _ => Array.Empty<string>()
        })
        {
            yield return alias;
        }

        if (normalized.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
        {
            var digit = normalized[6..];
            yield return $"Num {digit}";
            yield return $"Numpad {digit}";
            yield return $"Keypad {digit}";
            yield return $"KP {digit}";
            yield return $"KP{digit}";
            yield return $"Numpad{digit}";
            yield return $"NUMPAD{digit}";
        }
    }

}

public sealed record RgbDeviceModeSnapshot(int Index, uint? Speed, Direction? Direction, Color[] Colors);
public sealed record RgbLightingSnapshot(int DeviceIndex, Color[] Colors, RgbDeviceModeSnapshot Mode, int KeyIndex, Color IndicatorColor)
{
    public Guid Id { get; } = Guid.NewGuid();
}
public sealed record RgbKeyboardSnapshot(int DeviceIndex, Color[] Colors, RgbDeviceModeSnapshot Mode, Color IndicatorColor);
public sealed record OpenRgbAvailability(bool IsAvailable, string? Message, bool WasStarted = false);
