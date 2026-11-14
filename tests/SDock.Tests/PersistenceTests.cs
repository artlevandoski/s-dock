using Microsoft.Data.Sqlite;
using SDock.Core;

namespace SDock.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Library_and_profile_survive_restart()
    {
        var folder = _env.CreateSampleFolder();
        EmulatorProfile profile;
        List<(string, string, string, string?)> snapshot;
        string selected;
        using (var lib = _env.OpenLibrary())
        {
            LibraryScanner.ImportEntries(lib, folder, Generations.Switch1, LibraryScanner.ScanFolder(folder, Generations.Switch1, lib).Entries);
            profile = EmulatorProfile.New("Eden", @"C:\Program Files\Eden Emulator\eden.exe", LaunchArgs.EdenPreset, emulator: "eden");
            lib.SaveProfile(profile);
            selected = lib.ListEditions()[1].Id;
            lib.SetSetting("last_selected_edition", selected);
            snapshot = lib.ListEditions().Select(e => (e.Id, e.Title, e.Generation, e.PrimaryFile?.Path)).ToList();
        }

        using var reopened = _env.OpenLibrary();
        Assert.Equal(snapshot, reopened.ListEditions().Select(e => (e.Id, e.Title, e.Generation, e.PrimaryFile?.Path)).ToList());
        var p = Assert.Single(reopened.ListProfiles());
        Assert.True(p.SameSettingsAs(profile));
        Assert.Equal(selected, reopened.GetSetting("last_selected_edition"));
        Assert.Equal(Generations.Switch1, reopened.FolderGeneration(folder));
    }

    [Fact]
    public void Reserved_fields_and_separate_file_table()
    {
        using var lib = _env.OpenLibrary();
        var game = _env.Game("A Game.nsp");
        var id = lib.AddEdition("A Game", Generations.Switch2, game);
        var e = lib.GetEdition(id)!;
        Assert.Null(e.ConsoleTitleId);
        Assert.Null(e.IgdbId);
        Assert.Equal(game, Assert.Single(e.Files).Path);

        using var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('editions')";
        var cols = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) cols.Add(r.GetString(0));
        Assert.Contains("console_title_id", cols);
        Assert.Contains("igdb_id", cols);
        Assert.DoesNotContain("path", cols); // launch paths live in edition_files
    }

    [Fact]
    public void Relink_moved_file()
    {
        using var lib = _env.OpenLibrary();
        var old = Path.Combine(_env.Root, "old place", "Game.nsp");
        var id = lib.AddEdition("Game", Generations.Switch1, old);
        Assert.True(lib.GetEdition(id)!.IsMissing);
        var moved = _env.Game("Game.nsp");
        lib.RelinkFile(lib.GetEdition(id)!.PrimaryFile!.Id, moved);
        var e = lib.GetEdition(id)!;
        Assert.False(e.IsMissing);
        Assert.Equal(moved, e.PrimaryFile!.Path);
    }

    [Fact]
    public void Emulators_folder_is_the_ignored_repo_folder_when_run_from_source()
    {
        var root = AppPaths.FindSourceRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "emulators", "README.md")));
        Assert.Contains("/emulators/*", File.ReadAllText(Path.Combine(root, ".gitignore")));
        Assert.Null(AppPaths.FindSourceRoot(_env.Root)); // an installed copy falls back to LOCALAPPDATA
    }

    [Fact]
    public void Path_keys_match_the_python_build()
    {
        // Python: os.path.normcase(os.path.normpath(os.path.abspath(p)))
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"c:\games\my game.nsp", AppPaths.PathKey(@"C:/Games/./My Game.nsp"));
        Assert.Equal(@"c:\games", AppPaths.PathKey(@"C:\Games\"));
        Assert.Equal(@"c:\", AppPaths.PathKey(@"C:\"));
    }
}
