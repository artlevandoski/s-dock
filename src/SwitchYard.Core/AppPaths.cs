namespace SwitchYard.Core;

/// <summary>
/// Where Switch Yard keeps its own files (never game files).
///
/// Portable by default: everything lives in the "portable root", the repository when run from
/// source, otherwise the folder holding SwitchYard.exe (or, for an installed copy, the folder above
/// the installer's replaceable "current" folder). Inside it:
///   data\       library database and logs
///   emulators\  emulator builds the user places there
/// If that folder can't be written to (e.g. under Program Files), %LOCALAPPDATA%\SwitchYard is used instead.
/// </summary>
public static class AppPaths
{
    public const string DataDirEnv = "SWITCHYARD_DATA_DIR";
    public const string EmulatorsDirEnv = "SWITCHYARD_EMULATORS_DIR";
    public const string AppDirName = "SwitchYard";

    /// <summary>Where earlier builds kept their data, and the fallback when the portable root is read-only.</summary>
    public static string UserProfileDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName);

    private static readonly Lazy<string> _portableRoot = new(() => FindPortableRoot(AppContext.BaseDirectory));
    private static readonly Lazy<bool> _rootWritable = new(() => IsWritable(PortableRoot));

    /// <summary>The folder that holds data\ and emulators\ when running portably.</summary>
    public static string PortableRoot => _portableRoot.Value;

    /// <summary>True when data is kept in the portable root rather than the user profile.</summary>
    public static bool IsPortable =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirEnv)) && _rootWritable.Value;

    public static string FindPortableRoot(string baseDirectory)
    {
        if (FindSourceRoot(baseDirectory) is { } source)
            return source;
        var dir = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Installed copies (Velopack) run from <root>\current, which updates replace; keep data beside it.
        var parent = Path.GetDirectoryName(dir);
        if (string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase) &&
            parent is not null && File.Exists(Path.Combine(parent, "Update.exe")))
            return parent;
        return dir;
    }

    /// <summary>The repository root (folder holding SwitchYard.sln) above <paramref name="start"/>, if any.</summary>
    public static string? FindSourceRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SwitchYard.sln")))
                return dir.FullName;
        return null;
    }

    /// <summary>Can we create and delete a file in data\ under <paramref name="root"/>?</summary>
    public static bool IsWritable(string root)
    {
        try
        {
            var data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            var probe = Path.Combine(data, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Library database and logs. SWITCHYARD_DATA_DIR / --data-dir override everything.</summary>
    public static string DataDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(DataDirEnv);
            var dir = !string.IsNullOrWhiteSpace(overrideDir) ? overrideDir
                : _rootWritable.Value ? Path.Combine(PortableRoot, "data")
                : UserProfileDir;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDir, "library.sqlite3");

    /// <summary>Cached and user-supplied artwork, one folder per game.</summary>
    public static string ArtworkDir
    {
        get
        {
            var d = Path.Combine(DataDir, "artwork");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string LogDir
    {
        get
        {
            var d = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Where the user keeps emulator builds. SWITCHYARD_EMULATORS_DIR overrides it.</summary>
    public static string EmulatorsDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(EmulatorsDirEnv);
            var dir = !string.IsNullOrWhiteSpace(overrideDir) ? overrideDir
                : _rootWritable.Value ? Path.Combine(PortableRoot, "emulators")
                : Path.Combine(UserProfileDir, "Emulators");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// First portable start: copy the library an earlier build kept in %LOCALAPPDATA%\SwitchYard into
    /// <paramref name="dataDir"/>. The original is left in place as a backup. Returns the source copied, or null.
    /// </summary>
    public static string? CopyLegacyLibrary(string dataDir, string legacyDir)
    {
        var target = Path.Combine(dataDir, "library.sqlite3");
        var source = Path.Combine(legacyDir, "library.sqlite3");
        if (File.Exists(target) || !File.Exists(source) || PathKey(dataDir) == PathKey(legacyDir))
            return null;
        Directory.CreateDirectory(dataDir);
        File.Copy(source, target);
        return source;
    }

    /// <summary>
    /// Store paths inside the portable root relative to it ("emulators\Eden\eden.exe"), so they survive
    /// the folder being moved or getting a new drive letter. Paths elsewhere stay absolute.
    /// </summary>
    public static string ToStoredPath(string path, string? root)
    {
        if (root is null || string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return path;
        var full = Path.GetFullPath(path);
        var rootKey = PathKey(root) + Path.DirectorySeparatorChar;
        return PathKey(full).StartsWith(rootKey, StringComparison.Ordinal)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    /// <summary>Inverse of <see cref="ToStoredPath"/>: relative paths are resolved against the portable root.</summary>
    public static string FromStoredPath(string stored, string? root) =>
        root is null || string.IsNullOrWhiteSpace(stored) || Path.IsPathRooted(stored)
            ? stored
            : Path.GetFullPath(Path.Combine(root, stored));

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
