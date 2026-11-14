using SwitchYard.Core;

namespace SwitchYard.Tests;

public sealed class PortableTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Running_from_source_uses_the_repository_as_portable_root()
    {
        var root = AppPaths.FindPortableRoot(AppContext.BaseDirectory);
        Assert.True(File.Exists(Path.Combine(root, "SwitchYard.sln")));
        Assert.Contains("/data/", File.ReadAllText(Path.Combine(root, ".gitignore")));
    }

    [Fact]
    public void Packaged_build_uses_its_own_folder()
    {
        var app = Path.Combine(_env.Root, "SwitchYard");
        Directory.CreateDirectory(app);
        Assert.Equal(app, AppPaths.FindPortableRoot(app + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Installed_build_keeps_data_beside_the_replaceable_current_folder()
    {
        var install = Path.Combine(_env.Root, "Installed SwitchYard");
        var current = Path.Combine(install, "current");
        Directory.CreateDirectory(current);
        Assert.Equal(current, AppPaths.FindPortableRoot(current)); // no installer present: just the folder
        File.WriteAllText(Path.Combine(install, "Update.exe"), "");
        Assert.Equal(install, AppPaths.FindPortableRoot(current));
    }

    [Fact]
    public void Writable_check()
    {
        Assert.True(AppPaths.IsWritable(_env.Root));
        Assert.True(Directory.Exists(Path.Combine(_env.Root, "data")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_env.Root, "data"))); // probe file cleaned up
        var file = Path.Combine(_env.Root, "a file.txt");
        File.WriteAllText(file, "");
        Assert.False(AppPaths.IsWritable(file)); // a "folder" that is really a file can't hold data\
    }

    [Fact]
    public void First_portable_start_copies_the_old_library_once()
    {
        var legacy = Path.Combine(_env.Root, "LocalAppData", "SwitchYard");
        var data = Path.Combine(_env.Root, "Portable", "data");
        using (var lib = new LibraryStore(Path.Combine(legacy, "library.sqlite3")))
            lib.AddEdition("Kept Game", Generations.Switch1, _env.Game());

        Assert.Equal(Path.Combine(legacy, "library.sqlite3"), AppPaths.CopyLegacyLibrary(data, legacy));
        Assert.True(File.Exists(Path.Combine(legacy, "library.sqlite3"))); // original kept as backup
        using (var lib = new LibraryStore(Path.Combine(data, "library.sqlite3")))
            Assert.Equal("Kept Game", Assert.Single(lib.ListEditions()).Title);

        Assert.Null(AppPaths.CopyLegacyLibrary(data, legacy));    // never overwrites a portable library
        Assert.Null(AppPaths.CopyLegacyLibrary(legacy, legacy));  // same folder: nothing to do
        Assert.Null(AppPaths.CopyLegacyLibrary(Path.Combine(_env.Root, "fresh"), Path.Combine(_env.Root, "none")));
    }

    [Fact]
    public void Paths_inside_the_portable_root_are_stored_relative()
    {
        var root = Path.Combine(_env.Root, "SwitchYard");
        Assert.Equal(@"emulators\Eden\eden.exe", AppPaths.ToStoredPath(Path.Combine(root, "emulators", "Eden", "eden.exe"), root));
        Assert.Equal(@"D:\Emulators\eden.exe", AppPaths.ToStoredPath(@"D:\Emulators\eden.exe", root));
        Assert.Equal(Path.Combine(root + " Other", "x.exe"), AppPaths.ToStoredPath(Path.Combine(root + " Other", "x.exe"), root)); // prefix only
        Assert.Equal("", AppPaths.ToStoredPath("", root));
        Assert.Equal(@"D:\x.exe", AppPaths.ToStoredPath(@"D:\x.exe", null));
        Assert.Equal(Path.Combine(root, "emulators", "Eden", "eden.exe"), AppPaths.FromStoredPath(@"emulators\Eden\eden.exe", root));
    }

    [Fact]
    public void Emulator_profile_survives_the_folder_moving()
    {
        var oldRoot = Path.Combine(_env.Root, "USB E", "SwitchYard");
        var exe = Path.Combine(oldRoot, "emulators", "Eden-Windows", "eden.exe");
        var external = @"C:\Program Files\Other Emu\other.exe";
        var db = Path.Combine(oldRoot, "data", "library.sqlite3");
        using (var lib = new LibraryStore(db, oldRoot))
        {
            lib.SaveProfile(EmulatorProfile.New("Eden", exe, LaunchArgs.EdenPreset, workingDir: Path.GetDirectoryName(exe)!));
            lib.SaveProfile(EmulatorProfile.New("Other", external, LaunchArgs.EdenPreset));
        }

        // Copy the whole folder somewhere else (another drive letter, another PC).
        var newRoot = Path.Combine(_env.Root, "USB F", "SwitchYard");
        Directory.CreateDirectory(Path.Combine(newRoot, "data"));
        File.Copy(db, Path.Combine(newRoot, "data", "library.sqlite3"));
        using var moved = new LibraryStore(Path.Combine(newRoot, "data", "library.sqlite3"), newRoot);
        var profiles = moved.ListProfiles().ToDictionary(p => p.Name);
        Assert.Equal(Path.Combine(newRoot, "emulators", "Eden-Windows", "eden.exe"), profiles["Eden"].Executable);
        Assert.Equal(Path.Combine(newRoot, "emulators", "Eden-Windows"), profiles["Eden"].WorkingDir);
        Assert.Equal(external, profiles["Other"].Executable); // outside the folder: unchanged
    }
}
