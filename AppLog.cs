using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace AutoClicker;

internal static class AppLog
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object Sync = new();
    private static string? logPath;

    internal static string Path => logPath ??= ResolvePath();

    internal static void Start()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Write("INFO", $"AutoClicker starting | Version={version} | PID={Environment.ProcessId} | OS={Environment.OSVersion} | BaseDirectory={AppContext.BaseDirectory}");
    }

    internal static void Info(string message) => Write("INFO", message);
    internal static void Error(string context, Exception exception) => Write("ERROR", $"{context}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                var path = Path;
                RotateIfNeeded(path);
                File.AppendAllText(path, $"[{DateTime.UtcNow:O}] [{level}] [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never prevent the safety shutdown path.
        }
    }

    private static string ResolvePath()
    {
        var besideExecutable = System.IO.Path.Combine(AppContext.BaseDirectory, "AutoClicker.log");
        try
        {
            using (File.Open(besideExecutable, FileMode.Append, FileAccess.Write, FileShare.Read)) { }
            return besideExecutable;
        }
        catch
        {
            var fallbackDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker");
            Directory.CreateDirectory(fallbackDirectory);
            return System.IO.Path.Combine(fallbackDirectory, "AutoClicker.log");
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes) return;
            var previous = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "AutoClicker.previous.log");
            File.Move(path, previous, overwrite: true);
        }
        catch { }
    }
}
