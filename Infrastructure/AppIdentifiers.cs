// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public static class AppIdentity
{
    public const string Name = "AutoClicker";
    public const string CompactGuidFormat = "N";
}

public static class AppCommandLineOptions
{
    public const string EndToEnd = "--e2e";
    public const string RegisterEndToEndKeyboardHotkeys = "--e2e-register-keyboard-hotkeys";
    public const string ConfigDirectory = "--config-directory";
    public const string InstanceId = "--instance-id";
    public const string SaveFile = "--save-file";
    public const string OpenFile = "--open-file";
    public const string CrashWatchdog = "--crash-watchdog";
}

public static class AppModeIds
{
    public const string Simple = "Simple";
    public const string Advanced = "Advanced";
}

public static class NativeLibraryNames
{
    public const string User32 = "user32.dll";
    public const string Kernel32 = "kernel32.dll";
}
