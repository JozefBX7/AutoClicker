using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace AutoClicker;

internal static class AppLog
{
    // Keep enough history to diagnose a fault without turning logging into an
    // unbounded disk consumer: one current log and one prior log, 2 MB each.
    internal const long MaxLogBytes = 2 * 1024 * 1024;
    private const int MaxEntryCharacters = 12 * 1024;
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
                var safeMessage = message.Length <= MaxEntryCharacters
                    ? message
                    : message[..MaxEntryCharacters] + " [log entry truncated]";
                var entry = $"[{DateTime.UtcNow:O}] [{level}] [T{Environment.CurrentManagedThreadId}] {safeMessage}{Environment.NewLine}";
                // If another program has the file open without permitting a
                // rename, skip this entry. Logging must never enlarge a capped
                // file or interfere with the input/safety paths.
                if (!TryPrepareForAppend(path, Encoding.UTF8.GetByteCount(entry))) return;
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(entry);
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

    internal static bool TryPrepareForAppend(string path, long bytesToAppend = 0)
    {
        try
        {
            if (!File.Exists(path)) return true;
            var currentLength = new FileInfo(path).Length;
            if (currentLength < MaxLogBytes && bytesToAppend <= MaxLogBytes - currentLength) return true;
            var previous = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "AutoClicker.previous.log");
            File.Move(path, previous, overwrite: true);
            TrimToMaximum(previous);
            return true;
        }
        catch
        {
            // An editor, antivirus scanner, or diagnostic tool may hold the log
            // open. Do not append past the cap and never surface that failure to
            // the application.
            return false;
        }
    }

    private static void TrimToMaximum(string path)
    {
        if (new FileInfo(path).Length <= MaxLogBytes) return;
        var temporary = path + ".trim";
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bytesToKeep = Math.Min(MaxLogBytes, source.Length);
                source.Position = source.Length - bytesToKeep;
                using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Rotation has already succeeded, so keep the retained previous log
            // unchanged if a trim cannot safely complete. The next rotation will
            // replace it with a normal capped file.
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}
