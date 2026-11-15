using SDock.Core;

namespace SDock.Tests;

public sealed class PortableTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Running_from_source_uses_the_repository_as_portable_root()
    {
        var root = AppPaths.FindPortableRoot(AppContext.BaseDirectory);
        Assert.True(File.Exists(Path.Combine(root, "SDock.sln")));
        Assert.Contains("/data/", File.ReadAllText(Path.Combine(root, ".gitignore")));
    }

    [Fact]
    public void Packaged_build_uses_its_own_folder()
    {
        var app = Path.Combine(_env.Root, "SDock");
        Directory.CreateDirectory(app);
        Assert.Equal(app, AppPaths.FindPortableRoot(app + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Installed_build_keeps_data_beside_the_replaceable_current_folder()
    {
        var install = Path.Combine(_env.Root, "Installed SDock");
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
    public void Everything_stays_in_the_S_Dock_folder()
    {
        // The Velopack build: <S-Dock>\current\S-Dock.exe with Update.exe beside "current".
        var root = Path.Combine(_env.Root, "S-Dock");
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllText(Path.Combine(root, "Update.exe"), "");
        Assert.Equal(root, AppPaths.FindPortableRoot(Path.Combine(root, "current")));
        // A plain unzipped folder: the folder itself.
        var plain = Path.Combine(_env.Root, "Plain S-Dock");
        Directory.CreateDirectory(plain);
        Assert.Equal(plain, AppPaths.FindPortableRoot(plain));
        // No user-profile fallback exists any more: the code never asks Windows for AppData.
        var source = File.ReadAllText(Path.Combine(AppPaths.FindSourceRoot(AppContext.BaseDirectory)!, "src", "SDock.Core", "AppPaths.cs"));
        Assert.DoesNotContain("SpecialFolder", source);
    }

    [Fact]
    public void Paths_inside_the_portable_root_are_stored_relative()
    {
        var root = Path.Combine(_env.Root, "SDock");
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
        var oldRoot = Path.Combine(_env.Root, "USB E", "SDock");
        var exe = Path.Combine(oldRoot, "emulators", "Eden-Windows", "eden.exe");
        var external = @"C:\Program Files\Other Emu\other.exe";
        var db = Path.Combine(oldRoot, "data", "library.sqlite3");
        using (var lib = new LibraryStore(db, oldRoot))
        {
            lib.SaveProfile(EmulatorProfile.New("Eden", exe, LaunchArgs.EdenPreset, workingDir: Path.GetDirectoryName(exe)!));
            lib.SaveProfile(EmulatorProfile.New("Other", external, LaunchArgs.EdenPreset));
        }

        // Copy the whole folder somewhere else (another drive letter, another PC).
        var newRoot = Path.Combine(_env.Root, "USB F", "SDock");
        Directory.CreateDirectory(Path.Combine(newRoot, "data"));
        File.Copy(db, Path.Combine(newRoot, "data", "library.sqlite3"));
        using var moved = new LibraryStore(Path.Combine(newRoot, "data", "library.sqlite3"), newRoot);
        var profiles = moved.ListProfiles().ToDictionary(p => p.Name);
        Assert.Equal(Path.Combine(newRoot, "emulators", "Eden-Windows", "eden.exe"), profiles["Eden"].Executable);
        Assert.Equal(Path.Combine(newRoot, "emulators", "Eden-Windows"), profiles["Eden"].WorkingDir);
        Assert.Equal(external, profiles["Other"].Executable); // outside the folder: unchanged
    }

    [Fact]
    public void Separate_data_folder_still_resolves_emulators_inside_switch_yard()
    {
        // --data-dir puts the database elsewhere; relative emulator paths still mean "inside S-Dock's folder".
        var root = Path.Combine(_env.Root, "SDock");
        var exe = Path.Combine(root, "emulators", "Eden", "eden.exe");
        var elsewhere = Path.Combine(_env.Root, "Throwaway Data", "library.sqlite3");
        using (var lib = new LibraryStore(Path.Combine(root, "data", "library.sqlite3"), root))
            lib.SaveProfile(EmulatorProfile.New("Eden", exe, LaunchArgs.EdenPreset));
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.Copy(Path.Combine(root, "data", "library.sqlite3"), elsewhere);
        using var other = new LibraryStore(elsewhere, root);
        Assert.Equal(exe, Assert.Single(other.ListProfiles()).Executable);
    }
}
