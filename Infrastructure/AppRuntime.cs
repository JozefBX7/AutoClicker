// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;

namespace AutoClicker;

// Process-scoped runtime options. Production launches use the existing defaults; desktop tests opt in explicitly
// so they cannot read user configuration, collide with a running instance, or install global input hooks.
internal static class AppRuntime
{
    private static readonly object journalLock = new();
    internal static bool IsEndToEndTest { get; private set; }
    internal static bool RegisterEndToEndKeyboardHotkeys { get; private set; }
    internal static string? ConfigDirectoryOverride { get; private set; }
    internal static string? SaveFilePathOverride { get; private set; }
    internal static string? OpenFilePathOverride { get; private set; }
    internal static string InstanceId { get; private set; } = string.Empty;

    internal static void Configure(string[] args)
    {
        if (!HasArgument(args, "--e2e")) return;

        IsEndToEndTest = true;
        RegisterEndToEndKeyboardHotkeys = ShouldRegisterEndToEndKeyboardHotkeys(args);
        ConfigDirectoryOverride = ReadValue(args, "--config-directory") is { } directory
            ? Path.GetFullPath(directory)
            : throw new ArgumentException("End-to-end mode requires --config-directory.");
        InstanceId = SanitizeName(ReadValue(args, "--instance-id") ?? Environment.ProcessId.ToString());
        SaveFilePathOverride = ValidateTestFilePath(ReadValue(args, "--save-file"));
        OpenFilePathOverride = ValidateTestFilePath(ReadValue(args, "--open-file"));
    }

    internal static string ScopedKernelName(string productionName) =>
        IsEndToEndTest ? $"{productionName}.E2E.{InstanceId}" : productionName;

    internal static void RecordEndToEndEvent(string kind, string details)
    {
        if (!IsEndToEndTest || ConfigDirectoryOverride is null) return;
        var line = $"{DateTimeOffset.UtcNow:O}\t{kind}\t{details}{Environment.NewLine}";
        lock (journalLock)
        {
            Directory.CreateDirectory(ConfigDirectoryOverride);
            File.AppendAllText(Path.Combine(ConfigDirectoryOverride, "e2e-runtime-events.log"), line);
        }
    }

    private static string? ReadValue(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static string SanitizeName(string value)
    {
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? Environment.ProcessId.ToString() : safe;
    }

    private static string? ValidateTestFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = Path.GetFullPath(path);
        if (!IsPathWithinDirectory(ConfigDirectoryOverride!, fullPath))
            throw new ArgumentException("End-to-end file paths must stay inside the isolated configuration directory.");
        return fullPath;
    }

    internal static bool IsPathWithinDirectory(string directory, string path)
    {
        var configRoot = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(configRoot, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldRegisterEndToEndKeyboardHotkeys(IReadOnlyList<string> args) =>
        HasArgument(args, "--e2e") && HasArgument(args, "--e2e-register-keyboard-hotkeys");

    private static bool HasArgument(IEnumerable<string> args, string option) =>
        args.Any(argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
}
