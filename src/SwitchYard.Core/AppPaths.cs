namespace SwitchYard.Core;

/// <summary>Switch Yard's own user-writable data (never game files).</summary>
public static class AppPaths
{
    public const string DataDirEnv = "SWITCHYARD_DATA_DIR";
    public const string AppDirName = "SwitchYard";

    /// <summary>%LOCALAPPDATA%\SwitchYard, unless SWITCHYARD_DATA_DIR or --data-dir overrides it.</summary>
    public static string DataDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(DataDirEnv);
            var dir = !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDir, "library.sqlite3");

    public static string LogDir
    {
        get
        {
            var d = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>
    /// Case- and separator-insensitive key used to recognise the same file.
    /// Matches the keys written by the Milestone 1 Python build (lowercase, backslashes, no trailing separator).
    /// </summary>
    public static string PathKey(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }
}
