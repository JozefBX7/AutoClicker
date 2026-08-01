using OpenRGB.NET;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace AutoClicker;

public sealed class RgbSettings
{
    public bool Enabled { get; set; }
    public int DeviceIndex { get; set; } = -1;
    public string DeviceName { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool StopAutoStartedOnExit { get; set; } = true;
    public bool CrashRecoveryEnabled { get; set; } = true;
    public string IndicatorColor { get; set; } = "#22D3EE";
    public string LightingEffect { get; set; } = "Constant";
    public int PulseSpeedMilliseconds { get; set; } = 450;

    // "Pulse" was the former name for the on/off effect. Keep it as a
    // backwards-compatible alias so existing saved settings become Blink.
    public bool IsBlink => string.Equals(LightingEffect, "Blink", StringComparison.OrdinalIgnoreCase)
        || string.Equals(LightingEffect, "Pulse", StringComparison.OrdinalIgnoreCase);
    public bool IsPulse => string.Equals(LightingEffect, "Fade", StringComparison.OrdinalIgnoreCase);

    public RgbSettings Clone() => new()
    {
        Enabled = Enabled,
        DeviceIndex = DeviceIndex,
        DeviceName = DeviceName,
        AutoStart = AutoStart,
        StopAutoStartedOnExit = StopAutoStartedOnExit,
        CrashRecoveryEnabled = CrashRecoveryEnabled,
        IndicatorColor = IndicatorColor,
        LightingEffect = LightingEffect,
        PulseSpeedMilliseconds = PulseSpeedMilliseconds
    };
}

public sealed record KeyboardDevice(int Index, string Name)
{
    public override string ToString() => Name;
}

public static class OpenRgbHighlighter
{
    internal const int PulseFramesPerCycle = 12;
    internal const int MaximumPulseFramesPerCycle = 36;
    internal const int PulseTargetFrameDurationMilliseconds = 100;
    internal const int MinimumPulseCycleMilliseconds = 600;
    internal const int MaximumPulseCycleMilliseconds = 3500;
    internal const int SolidPreviewDurationMilliseconds = 5000;
    // Never stop an OpenRGB instance we did not start.
    private static readonly object StartedProcessLock = new();
    private static readonly SemaphoreSlim SdkStartupLock = new(1, 1);
    private static Process? processStartedByAutoClicker;

    internal static bool ShouldStartOnApplicationLaunch(RgbSettings settings) => settings.Enabled && settings.AutoStart;

    public static async Task<OpenRgbAvailability> EnsureSdkAsync(RgbSettings settings)
    {
        if (await IsSdkAvailableAsync()) return new(true, null);
        if (!settings.AutoStart)
            return new(false, "OpenRGB's SDK server is not available. Enable it in OpenRGB, or turn on automatic startup here.");

        await SdkStartupLock.WaitAsync();
        try
        {
            // Another request may have started OpenRGB while this caller was waiting.
            if (await IsSdkAvailableAsync()) return new(true, null);

            if (Process.GetProcessesByName("OpenRGB").Length > 0)
                return new(false, "OpenRGB is already running, but its SDK server is not available. Enable its SDK server on port 6742, then refresh.");

            var executable = FindOpenRgbExecutable();
            if (executable is null)
                return new(false, "OpenRGB is not installed in its usual location. Install OpenRGB, start it once, then refresh.");

            try
            {
                var process = Process.Start(new ProcessStartInfo(executable, "--server")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                });
                if (process is null) return new(false, "OpenRGB could not be started.");
                lock (StartedProcessLock) processStartedByAutoClicker = process;
            }
            catch (Exception exception)
            {
                return new(false, $"Could not start OpenRGB: {exception.Message}");
            }

            // Wait briefly for the SDK socket.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(200);
                if (await IsSdkAvailableAsync()) return new(true, "OpenRGB was started automatically.");
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

    private static async Task<bool> IsSdkAvailableAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            await client.ConnectAsync(IPAddress.Loopback, 6742, timeout.Token);
            return true;
        }
        catch { return false; }
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
        using var client = new OpenRgbClient(name: "AutoClicker");
        return client.GetAllControllerData()
            .Where(device => device.Type == DeviceType.Keyboard || LooksLikeKeyboard(device.Name) || LooksLikeKeyboard(device.Vendor))
            .Select(device => new KeyboardDevice(device.Index, device.Name))
            .ToArray();
    }

    public static RgbLightingSnapshot? EnableKeyIndicator(RgbSettings settings, string keyName, out string? error, bool lightImmediately = true)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: "AutoClicker");
            var keyboard = client.GetControllerData(settings.DeviceIndex);

            var led = keyboard.Leds.FirstOrDefault(item => SameKey(item.Name, keyName));
            if (led is null) { error = $"OpenRGB could not light {keyName} on {keyboard.Name}. Choose a standard keyboard key, then try again."; return null; }
            if (keyboard.Colors.Length != keyboard.Leds.Length) { error = "This keyboard does not expose per-key colours to OpenRGB."; return null; }

            // Restore the original keyboard colours when finished.
            var snapshot = new RgbLightingSnapshot(keyboard.Index, keyboard.Colors.ToArray(), led.Index, IndicatorColor(settings));
            if (lightImmediately)
            {
                var colors = snapshot.Colors.ToArray();
                colors[snapshot.KeyIndex] = snapshot.IndicatorColor;
                client.SetCustomMode(snapshot.DeviceIndex);
                client.UpdateLeds(snapshot.DeviceIndex, colors);
            }
            return snapshot;
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB indicator update failed", exception);
            error = $"OpenRGB unavailable: {exception.Message}";
            return null;
        }
    }

    public static bool CanHighlightKey(RgbSettings settings, string keyName, out string? error)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: "AutoClicker");
            var keyboard = client.GetControllerData(settings.DeviceIndex);
            if (!keyboard.Leds.Any(item => SameKey(item.Name, keyName)))
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
            error = $"OpenRGB unavailable: {exception.Message}";
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
            using var client = new OpenRgbClient(name: "AutoClicker");
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
            using var client = new OpenRgbClient(name: "AutoClicker");
            client.SetCustomMode(snapshot.DeviceIndex);
            client.UpdateLeds(snapshot.DeviceIndex, snapshot.Colors);
        }
        catch (Exception exception) { AppLog.Error("OpenRGB indicator restore failed", exception); }
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
                if (lit) RestoreKey(snapshot); else LightIndicator(snapshot);
                lit = !lit;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("OpenRGB blink effect failed", exception); }
    }

    public static async Task FadePulseIndicatorAsync(RgbLightingSnapshot snapshot, int cycleMilliseconds, CancellationToken cancellation)
    {
        var cycle = Math.Clamp(cycleMilliseconds, MinimumPulseCycleMilliseconds, MaximumPulseCycleMilliseconds);
        var framesPerCycle = GetPulseFramesPerCycle(cycle);
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

    internal static int GetPulseFramesPerCycle(int cycleMilliseconds)
    {
        var targetFrames = (int)Math.Ceiling(cycleMilliseconds / (double)PulseTargetFrameDurationMilliseconds);
        return Math.Clamp(targetFrames, PulseFramesPerCycle, MaximumPulseFramesPerCycle);
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

    public static async Task FadePulseKeyboardAsync(RgbKeyboardSnapshot snapshot, int cycleMilliseconds, CancellationToken cancellation)
    {
        var cycle = Math.Clamp(cycleMilliseconds, MinimumPulseCycleMilliseconds, MaximumPulseCycleMilliseconds);
        var framesPerCycle = GetPulseFramesPerCycle(cycle);
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

    private static void LightIndicator(RgbLightingSnapshot snapshot)
    {
        using var client = new OpenRgbClient(name: "AutoClicker");
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateSingleLed(snapshot.DeviceIndex, snapshot.KeyIndex, snapshot.IndicatorColor);
    }

    private static RgbKeyboardSnapshot? CaptureKeyboardForTest(RgbSettings settings, out string? error)
    {
        error = null;
        try
        {
            using var client = new OpenRgbClient(name: "AutoClicker");
            var keyboard = client.GetControllerData(settings.DeviceIndex);
            if (keyboard.Colors.Length == 0)
            {
                error = $"{keyboard.Name} does not expose colours that OpenRGB can test.";
                return null;
            }
            return new RgbKeyboardSnapshot(keyboard.Index, keyboard.Colors.ToArray(), IndicatorColor(settings));
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB keyboard test setup failed", exception);
            error = $"OpenRGB unavailable: {exception.Message}";
            return null;
        }
    }

    private static void LightKeyboard(RgbKeyboardSnapshot snapshot)
    {
        using var client = new OpenRgbClient(name: "AutoClicker");
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateLeds(snapshot.DeviceIndex, CreateKeyboardFlashColors(snapshot.Colors, snapshot.IndicatorColor));
    }

    public static void RestoreKeyboard(RgbKeyboardSnapshot snapshot)
    {
        try
        {
            using var client = new OpenRgbClient(name: "AutoClicker");
            client.SetCustomMode(snapshot.DeviceIndex);
            client.UpdateLeds(snapshot.DeviceIndex, snapshot.Colors);
        }
        catch (Exception exception) { AppLog.Error("OpenRGB keyboard test restore failed", exception); }
    }

    private static void RestoreKey(RgbLightingSnapshot snapshot)
    {
        using var client = new OpenRgbClient(name: "AutoClicker");
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateSingleLed(snapshot.DeviceIndex, snapshot.KeyIndex, snapshot.Colors[snapshot.KeyIndex]);
    }

    private static void SetIndicatorBlend(RgbLightingSnapshot snapshot, double strength)
    {
        using var client = new OpenRgbClient(name: "AutoClicker");
        client.SetCustomMode(snapshot.DeviceIndex);
        client.UpdateSingleLed(snapshot.DeviceIndex, snapshot.KeyIndex, BlendColor(snapshot.Colors[snapshot.KeyIndex], snapshot.IndicatorColor, strength));
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
        using var client = new OpenRgbClient(name: "AutoClicker");
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
            "RETURN" => new[] { "Enter", "Return" },
            "CAPITAL" => new[] { "Caps Lock", "CapsLock", "Caps" },
            "PRIOR" => new[] { "Page Up", "PageUp", "PgUp" },
            "NEXT" => new[] { "Page Down", "PageDown", "PgDn" },
            "SNAPSHOT" => new[] { "Print Screen", "PrintScreen", "PrtSc", "PrtScn" },
            "SCROLL" => new[] { "Scroll Lock", "ScrollLock", "ScrLk" },
            "NUMLOCK" => new[] { "Num Lock", "NumLock" },
            "DIVIDE" => new[] { "Num /", "Numpad /", "Keypad /" },
            "MULTIPLY" => new[] { "Num *", "Numpad *", "Keypad *" },
            "SUBTRACT" => new[] { "Num -", "Numpad -", "Keypad -" },
            "ADD" => new[] { "Num +", "Numpad +", "Keypad +" },
            "DECIMAL" => new[] { "Num .", "Numpad .", "Keypad ." },
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
        }
    }

    private static bool LooksLikeKeyboard(string? value)
    {
        var name = value ?? string.Empty;
        return name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
            || name.Contains("corsair", StringComparison.OrdinalIgnoreCase)
            || name.Contains("k70", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RgbLightingSnapshot(int DeviceIndex, Color[] Colors, int KeyIndex, Color IndicatorColor);
public sealed record RgbKeyboardSnapshot(int DeviceIndex, Color[] Colors, Color IndicatorColor);
public sealed record OpenRgbAvailability(bool IsAvailable, string? Message);
