// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace AutoClicker.E2E;

internal sealed class AutoClickerE2ESession : IDisposable
{
    private AutoClickerE2ESession(Application application, UIA3Automation automation, Window window)
    {
        Application = application;
        Automation = automation;
        Window = window;
        Editor = new ProfileEditorRobot(automation, window);
    }

    internal Application Application { get; }
    internal UIA3Automation Automation { get; }
    internal Window Window { get; }
    internal ProfileEditorRobot Editor { get; }

    internal AutomationElement MainElement(string automationId) => WaitUntilNotNull(
        () => Window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
        $"main-window element '{automationId}' was not found");

    internal AutomationElement DesktopElement(string automationId) => WaitUntilNotNull(
        () => TryDesktopElement(automationId),
        $"desktop element '{automationId}' was not found");

    internal AutomationElement? TryDesktopElement(string automationId) =>
        Automation.GetDesktop().FindFirstDescendant(condition =>
            condition.ByAutomationId(automationId).And(condition.ByProcessId(Application.ProcessId)));

    internal AutomationElement DesktopElementByName(string name) => WaitUntilNotNull(
        () => Automation.GetDesktop().FindFirstDescendant(condition =>
            condition.ByName(name).And(condition.ByProcessId(Application.ProcessId))),
        $"desktop element named '{name}' was not found");

    internal Window Dialog(string title) => WaitUntilNotNull(
        () => FindApplicationWindow(title) is var handle && handle != 0
            ? Automation.FromHandle(handle).AsWindow()
            : null,
        $"dialog '{title}' was not found");

    internal bool IsDialogOpen(string title) => FindApplicationWindow(title) != 0;

    internal void SendRegisteredKeyboardHotkey(VirtualKeyShort key)
    {
        using var control = Keyboard.Pressing(VirtualKeyShort.CONTROL);
        using var shift = Keyboard.Pressing(VirtualKeyShort.SHIFT);
        Keyboard.Press(key);
    }

    internal IDisposable HoldRegisteredKeyboardHotkey(VirtualKeyShort key)
    {
        var held = new List<IDisposable>();
        try
        {
            held.Add(Keyboard.Pressing(VirtualKeyShort.CONTROL));
            held.Add(Keyboard.Pressing(VirtualKeyShort.SHIFT));
            held.Add(Keyboard.Pressing(key));
            held.Reverse();
            return new HeldKeyboardChord(held.ToArray());
        }
        catch
        {
            foreach (var pressed in held.AsEnumerable().Reverse())
                try { pressed.Dispose(); } catch { }
            throw;
        }
    }

    internal void WaitFor(Func<bool> condition, string failure) => WaitUntil(condition, failure);

    internal static AutoClickerE2ESession Launch(
        string configDirectory,
        string? saveFile = null,
        string? openFile = null,
        bool registerKeyboardHotkeys = false)
    {
        var executable = ResolveExecutablePath();
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        startInfo.ArgumentList.Add("--e2e");
        startInfo.ArgumentList.Add("--config-directory");
        startInfo.ArgumentList.Add(configDirectory);
        startInfo.ArgumentList.Add("--instance-id");
        startInfo.ArgumentList.Add(Guid.NewGuid().ToString("N"));
        if (registerKeyboardHotkeys) startInfo.ArgumentList.Add("--e2e-register-keyboard-hotkeys");
        if (saveFile is not null)
        {
            startInfo.ArgumentList.Add("--save-file");
            startInfo.ArgumentList.Add(saveFile);
        }
        if (openFile is not null)
        {
            startInfo.ArgumentList.Add("--open-file");
            startInfo.ArgumentList.Add(openFile);
        }

        var application = Application.Launch(startInfo);
        var automation = new UIA3Automation();
        try
        {
            var window = WaitForMainWindow(application, automation, TimeSpan.FromSeconds(15));
            window.Focus();
            return new AutoClickerE2ESession(application, automation, window);
        }
        catch
        {
            Terminate(application);
            automation.Dispose();
            application.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Terminate(Application);
        Automation.Dispose();
        Application.Dispose();
    }

    private static void Terminate(Application application)
    {
        try
        {
            using var process = Process.GetProcessById(application.ProcessId);
            if (process.HasExited) return;
            application.Kill();
            if (process.WaitForExit(5_000)) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static Window WaitForMainWindow(Application application, UIA3Automation automation, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (application.HasExited) throw new InvalidOperationException("AutoClicker exited before its main window opened.");
            var window = application.GetMainWindow(automation);
            if (window is not null) return window;
            Thread.Sleep(100);
        }
        throw new TimeoutException("AutoClicker did not expose its main window within the timeout.");
    }

    private static string ResolveExecutablePath()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var executable = Path.Combine(repositoryRoot, "bin", configuration, "net8.0-windows", "AutoClicker.exe");
        return File.Exists(executable)
            ? executable
            : throw new FileNotFoundException("Build AutoClicker before running its desktop tests.", executable);
    }

    private static T WaitUntilNotNull<T>(Func<T?> find, string failure) where T : class
    {
        T? result = null;
        WaitUntil(() => (result = find()) is not null, failure);
        return result!;
    }

    private static void WaitUntil(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(75);
        }
        throw new TimeoutException(failure);
    }

    private nint FindApplicationWindow(string title)
    {
        nint handle = 0;
        while ((handle = FindWindowEx(0, handle, null, title)) != 0)
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == Application.ProcessId) return handle;
        }
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    private sealed class HeldKeyboardChord(params IDisposable[] keys) : IDisposable
    {
        private IDisposable[]? heldKeys = keys;

        public void Dispose()
        {
            var releasing = Interlocked.Exchange(ref heldKeys, null);
            if (releasing is null) return;
            Exception? failure = null;
            foreach (var key in releasing)
                try { key.Dispose(); }
                catch (Exception exception) { failure ??= exception; }
            if (failure is not null) throw failure;
        }
    }

}
