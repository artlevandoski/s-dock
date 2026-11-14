using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using SDock.App.ViewModels;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>TitleDB's region fallbacks and AeroFoil's fork, nx-versions, and the updates beside each game.</summary>
public sealed class VersionsTests : IDisposable
{
    private const string Hat = "010056E00853A000", Croc = "01006D9026E1E000", JpOnly = "01009CF012D50000";
    private readonly TestEnv _env = new();
    private readonly FakeTitleDb _fake = new();
    public void Dispose() => _env.Dispose();

    private string Folder => Path.Combine(_env.Root, "titledb");

    private void Seed()
    {
        _fake.Json = FakeTitleDb.File(FakeTitleDb.Entry("1", Hat, "A Hat in Time"));
        _fake.Listings["GB.en"] = FakeTitleDb.File(
            FakeTitleDb.Entry("2", Hat, "A Hat in Time (UK)"),
            FakeTitleDb.Entry("3", Croc, "Croc 2", description: "UK text."));
        _fake.Listings["JP.ja"] = FakeTitleDb.File(FakeTitleDb.Entry("4", JpOnly, "Jump King", description: "日本語の説明"));
        _fake.NxVersions = "id|version\n010056E00853A800|262144\n010056E00853B001|65536\n01006D9026E1E800|65536\n";
        _fake.NxChangelog = """
            ## 2026-11-14 09:46
            ```
            [Updated: 1]
            [010056E00853A800][v196608]->[v262144]
            ```
            ## 2024-03-02 10:00
            ```
            [Added: 2]
            [010056E00853A800][v196608]
            [01006D9026E1E800][v65536]
            ```
            ## 2019-01-05 10:00
            ```
            [Added: 1]
            [010056E00853A800][v65536]
            ```
            """;
    }

    // ------------------------------------------------------------- TitleDB listings
    [Fact]
    public async Task Regions_fill_gaps_and_japanese_lends_only_pictures()
    {
        Seed();
        using var client = _fake.Client(Folder);
        Assert.True(await client.DownloadAsync());
        var found = TitleDbClient.Lookup(client.FilePath, [Hat, Croc, JpOnly]);
        Assert.Equal("A Hat in Time", found[Hat].Name);             // US wins
        Assert.Equal("UK text.", found[Croc].Description);          // only in the UK listing
        Assert.Equal("GB.en", found[Croc].SourceRegion);
        Assert.Null(found[JpOnly].Description);                     // Japanese text left out
        Assert.NotNull(found[JpOnly].IconUrl);
        Assert.Contains(_fake.Requests, r => r.StartsWith(TitleDbClient.ForkBase));
        Assert.False(await client.DownloadAsync());                 // nothing changed: not rebuilt
    }

    [Fact]
    public async Task Original_titledb_is_used_when_the_fork_is_down()
    {
        Seed();
        _fake.ForkDown = true;
        using var client = _fake.Client(Folder);
        Assert.True(await client.DownloadAsync());
        Assert.Contains(_fake.Requests, r => r.StartsWith(TitleDbClient.UpstreamBase));
        Assert.Single(TitleDbClient.Lookup(client.FilePath, [Croc]));
    }

    // ------------------------------------------------------------- nx-versions
    [Theory]
    [InlineData("010056E00853A000", "010056E00853A000")]
    [InlineData("010056E00853A800", "010056E00853A000")]
    [InlineData("010056E00853B001", "010056E00853A000")]
    [InlineData("010056E00853B07F", "010056E00853A000")]
    [InlineData("nonsense", null)]
    public void Base_of_game_update_and_dlc(string id, string? expected) => Assert.Equal(expected, NxVersions.BaseOf(id));

    [Fact]
    public async Task Nx_versions_latest_update_and_dates()
    {
        Seed();
        using var client = _fake.Client(Folder);
        Assert.True(await NxVersions.DownloadAsync(client));
        var v = NxVersions.Load(Folder);
        Assert.Equal(new NxVersionInfo(262144, 20261114, 20190105), v[Hat]);
        Assert.Equal(new NxVersionInfo(65536, 20240302, 20240302), v[Croc]);
        Assert.Equal("update 4 (v262144)", NxVersions.Describe(262144));

        _fake.Offline = true; // a failed refresh keeps the saved copy
        Assert.False(await NxVersions.DownloadAsync(client));
        Assert.Equal(262144, NxVersions.Load(Folder)[Hat].LatestUpdate);
    }

    // ------------------------------------------------------------- updates beside the game
    private string GameFolder(string name, string id, params string[] extras)
    {
        var dir = Path.Combine(_env.Root, "Games", $"{name} [{id}]");
        Directory.CreateDirectory(Path.Combine(dir, "Updates"));
        Directory.CreateDirectory(Path.Combine(dir, "DLC"));
        File.WriteAllBytes(Path.Combine(dir, $"{name} [{id}] [v0] [BASE].nsp"), [1]);
        foreach (var extra in extras) File.WriteAllBytes(Path.Combine(dir, extra), [1]);
        return Path.Combine(dir, $"{name} [{id}] [v0] [BASE].nsp");
    }

    [Fact]
    public void Updates_and_dlc_are_read_from_the_game_folder()
    {
        using var lib = _env.OpenLibrary();
        var hat = lib.AddEdition("A Hat in Time", Generations.Switch1, GameFolder("A Hat in Time", Hat,
            @"Updates\A Hat in Time [010056E00853A800] [UPDATE][v196608].nsp",
            @"Updates\A Hat in Time [010056E00853A800] [UPDATE][v65536].nsp",
            @"DLC\A Hat in Time - Seal the Deal [010056E00853B001] [DLC][v0].nsp",
            @"DLC\A Hat in Time [010056E00853A000] - Nyakuza Metro [010056E00853B002] [DLC][v65536].nsp"), consoleTitleId: Hat);
        var croc = lib.AddEdition("Croc 2", Generations.Switch1, GameFolder("Croc 2", Croc), consoleTitleId: Croc);
        var index = LocalContent.Index(lib.ListEditions());
        Assert.Equal(new InstalledContent(196608, 2), index[hat]);
        Assert.Equal(new InstalledContent(null, 0), index[croc]);
    }

    [AvaloniaFact]
    public async Task Update_status_filter_and_release_year_fallback()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var hat = lib.AddEdition("A Hat in Time", Generations.Switch1, GameFolder("A Hat in Time", Hat,
            @"Updates\A Hat in Time [010056E00853A800] [UPDATE][v196608].nsp",
            @"DLC\A Hat in Time - Seal the Deal [010056E00853B001] [DLC][v0].nsp"), consoleTitleId: Hat);
        var croc = lib.AddEdition("Croc 2", Generations.Switch1, GameFolder("Croc 2", Croc,
            @"Updates\Croc 2 [01006D9026E1E800] [UPDATE][v65536].nsp"), consoleTitleId: Croc);
        var jp = lib.AddEdition("Jump King", Generations.Switch1, GameFolder("Jump King", JpOnly), consoleTitleId: JpOnly);
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi(), _ => null, _ => null, () => _fake.Client(Folder));

        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.StartsWith("TitleDB: 3 found", vm.StatusText);
        vm.InstalledContentReady(LocalContent.Index(lib.ListEditions()));

        vm.Select(hat);
        Assert.Equal("Update available: update 4 (v262144), out 2026-11-14 (you have update 3 (v196608))  ·  1 DLC", vm.UpdateStatusText);
        Assert.Contains("Update available", vm.SelectedTags);
        vm.Select(croc);
        Assert.Equal("Up to date: update 1 (v65536)", vm.UpdateStatusText);
        Assert.Equal("", lib.GetMetadata(jp).DisplaySummary ?? ""); // no Japanese description

        vm.MoreFilter = vm.MoreFilters.First(f => f.Value == FilterOption.UpdateAvailableValue);
        Assert.Equal(["A Hat in Time"], vm.Items.Select(i => i.Title));

        // nx-versions dates fill a missing release year (TitleDB's date wins when it has one).
        lib.SaveMetadata(lib.GetMetadata(croc) with { TdbReleaseDate = null });
        Assert.Equal(2024, lib.GetMetadata(croc).DisplayReleaseYear);
    }

    [AvaloniaFact]
    public async Task Turning_nx_versions_off_stops_downloading_it_and_keeps_what_was_found()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var hat = lib.AddEdition("A Hat in Time", Generations.Switch1, _env.Game("Hat.nsp"), consoleTitleId: Hat);
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi(), _ => null, _ => null, () => _fake.Client(Folder));
        Assert.True(vm.UseNxVersions);
        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.Equal(262144, lib.GetMetadata(hat).NxLatestUpdate);

        vm.ToggleNxVersionsCommand.Execute(null);
        Assert.False(vm.UseNxVersions);
        _fake.Requests.Clear();
        _fake.ETag = "\"v2\"";
        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.DoesNotContain(_fake.Requests, r => r.Contains("nx-versions"));
        Assert.Equal(262144, lib.GetMetadata(hat).NxLatestUpdate);
    }

    [Fact]
    public void Upgrade_from_v8_adds_nx_columns()
    {
        string id;
        using (var lib = _env.OpenLibrary())
        {
            id = lib.AddEdition("Kept", Generations.Switch1, _env.Game());
            lib.SaveMetadata(EditionMetadata.Empty(id) with { TdbName = "Kept" });
        }
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE edition_metadata DROP COLUMN nx_latest_update; ALTER TABLE edition_metadata DROP COLUMN nx_update_date; " +
                              "ALTER TABLE edition_metadata DROP COLUMN nx_first_date; UPDATE meta SET value = '8' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Equal("Kept", upgraded.GetMetadata(id).TdbName);
        upgraded.SaveMetadata(upgraded.GetMetadata(id) with { NxLatestUpdate = 65536 });
        Assert.Equal(65536, upgraded.GetMetadata(id).NxLatestUpdate);
    }
}
