// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

public partial class App : System.Windows.Application
{
    // Shared across processes.
    private const string InstanceMutexName = "Local\\AutoClicker.Singleton";
    private const string ActivateEventName = "Local\\AutoClicker.Activate";
    private System.Threading.Mutex? instanceMutex;
    private System.Threading.EventWaitHandle? activateEvent;
    private bool exiting;
    private bool crashing;

    public App()
    {
        AppLog.Start();
        DispatcherUnhandledException += (_, eventArgs) => { AppLog.Error("Unhandled WPF dispatcher exception", eventArgs.Exception); eventArgs.Handled = true; ExitAfterCrash(); };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => { if (eventArgs.ExceptionObject is Exception exception) AppLog.Error("Unhandled AppDomain exception", exception); else AppLog.Info($"Unhandled AppDomain error: {eventArgs.ExceptionObject}"); ExitAfterCrash(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { AppLog.Info("Process exit requested."); EmergencyStop(); };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => { AppLog.Error("Unobserved task exception", eventArgs.Exception); eventArgs.SetObserved(); };
    }

    private void EmergencyStop()
    {
        if (!Dispatcher.CheckAccess()) return;
        if (Current?.MainWindow is MainWindow window) window.EmergencyStop();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppRuntime.Configure(e.Args);
        if (CrashRecovery.TryRunWatchdog(e.Args))
        {
            Shutdown();
            return;
        }
        ThemeManager.Load();
        // A second launch brings the existing window forward.
        var instanceMutexName = AppRuntime.ScopedKernelName(InstanceMutexName);
        var activateEventName = AppRuntime.ScopedKernelName(ActivateEventName);
        instanceMutex = new System.Threading.Mutex(true, instanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try { System.Threading.EventWaitHandle.OpenExisting(activateEventName).Set(); } catch (System.Threading.WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        activateEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, activateEventName);
        _ = Task.Run(ListenForActivation);
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (!AppRuntime.IsEndToEndTest) CrashRecovery.StartIfEnabled();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Window closing normally performs this cleanup. Keep an idempotent UI-thread
        // fallback for shutdown paths that do not complete the window closing handler.
        EmergencyStop();
        AppLog.Info($"Application exit | Code={e.ApplicationExitCode}");
        if (!crashing) CrashRecovery.MarkCleanShutdown();
        exiting = true;
        activateEvent?.Set();
        activateEvent?.Dispose();
        try { instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ListenForActivation()
    {
        while (!exiting && activateEvent?.WaitOne() == true)
        {
            if (!exiting) Dispatcher.BeginInvoke(() => { if (MainWindow is AutoClicker.MainWindow window) window.BringToFront(); });
        }
    }

    private void ExitAfterCrash()
    {
        if (crashing) return;
        crashing = true;
        EmergencyStop();
        CrashRecovery.ExitAfterCrash();
    }
}
