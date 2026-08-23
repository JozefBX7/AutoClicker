// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.IO;

namespace AutoClicker;

internal static class AppPaths
{
    private const string PortableMarkerName = "portable.flag";
    // Portable builds keep their data beside the executable.
    internal static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarkerName));
    private static readonly string configDirectory = ResolveConfigDirectory();

    internal static string ConfigDirectory => AppRuntime.ConfigDirectoryOverride ?? configDirectory;
    internal static string ConfigFile(string fileName) => Path.Combine(ConfigDirectory, fileName);

    internal static string InstalledConfigDirectory(string localApplicationData) => Path.Combine(localApplicationData, AppIdentity.Name);
    internal static string PortableConfigDirectory(string baseDirectory) => Path.Combine(baseDirectory, "Data");

    private static string ResolveConfigDirectory()
    {
        // A read-only portable location falls back to per-user storage.
        var fallback = InstalledConfigDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!IsPortable) return fallback;

        var portableDirectory = PortableConfigDirectory(AppContext.BaseDirectory);
        try
        {
            Directory.CreateDirectory(portableDirectory);
            return portableDirectory;
        }
        catch
        {
            // A portable build launched from a read-only location still works;
            // it simply falls back to the standard per-user configuration store.
            return fallback;
        }
    }
}
