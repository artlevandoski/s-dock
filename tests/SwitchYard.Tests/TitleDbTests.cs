using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using SwitchYard.App.ViewModels;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.Tests;

/// <summary>Stand-in for TitleDB's file on GitHub and Nintendo's image server.</summary>
public sealed class FakeTitleDb : HttpMessageHandler
{
    public string Json { get; set; } = "{}";
    public string ETag { get; set; } = "\"v1\"";
    public bool Offline { get; set; }
    public List<string> Requests { get; } = [];
    public HashSet<string> BrokenImages { get; } = [];

    public static string Entry(string nsuId, string? titleId, string name, string? description = "About the game.",
        bool icon = true, bool demo = false) => $$"""
        "{{nsuId}}": {
            "bannerUrl": "https://img-eshop.cdn.nintendo.net/i/banner-{{nsuId}}.jpg",
            "category": ["Action", "Platformer", "Other"],
            "description": {{(description is null ? "null" : $"\"{description}\"")}},
            "developer": null,
            "iconUrl": {{(icon ? $"\"https://img-eshop.cdn.nintendo.net/i/icon-{nsuId}.jpg\"" : "null")}},
            "id": {{(titleId is null ? "null" : $"\"{titleId}\"")}},
            "intro": "Tagline",
            "isDemo": {{(demo ? "true" : "false")}},
            "languages": ["en"],
            "name": "{{name}}",
            "nsuId": {{nsuId}},
            "numberOfPlayers": 2,
            "publisher": "Example Publisher",
            "releaseDate": 20190412,
            "screenshots": ["https://img-eshop.cdn.nintendo.net/i/shot-{{nsuId}}.jpg"],
            "size": 1000
        }
        """;

    public static string File(params string[] entries) => "{\n" + string.Join(",\n", entries) + "\n}";

    public TitleDbClient Client(string folder) => new(folder, this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        if (Offline) throw new HttpRequestException("No connection");
        if (url == TitleDbClient.SourceUrl)
        {
            if (request.Headers.TryGetValues("If-None-Match", out var tags) && tags.Contains(ETag))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json, Encoding.UTF8) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ETag);
            return Task.FromResult(response);
        }
        if (url.StartsWith("https://img-eshop.cdn.nintendo.net/") && !BrokenImages.Contains(url))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FakeIgdb.TinyPng) });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class TitleDbTests : IDisposable
{
    private const string Zelda = "01007EF00011E000", Kirby = "01004D300C5AE000", Missing = "0100000000001000";
    private readonly TestEnv _env = new();
    private readonly FakeTitleDb _fake = new();
    public void Dispose() => _env.Dispose();

    private string Folder => Path.Combine(_env.Root, "titledb"); // per test: the data folder is shared
    private static string ArtDir => AppPaths.ArtworkDir;

    private void Seed() => _fake.Json = FakeTitleDb.File(
        FakeTitleDb.Entry("70010000000025", Zelda, "Breath of the Wild"),
        FakeTitleDb.Entry("70010000000026", Zelda, "Breath of the Wild (Demo)", description: null, demo: true),
        FakeTitleDb.Entry("70010000000027", null, "No title ID"),
        FakeTitleDb.Entry("70010000000030", Kirby, "Kirby", icon: false));

    // ------------------------------------------------------------- file and lookup
    [Fact]
    public async Task Download_saves_the_file_and_skips_an_unchanged_copy()
    {
        Seed();
        using var client = _fake.Client(Folder);
        Assert.False(client.HasFile);
        Assert.True(await client.DownloadAsync());
        Assert.True(client.HasFile);
        Assert.False(await client.DownloadAsync()); // same ETag: not downloaded again
        _fake.ETag = "\"v2\"";
        Assert.True(await client.DownloadAsync());
    }

    [Fact]
    public async Task Lookup_finds_title_ids_and_prefers_the_full_listing()
    {
        Seed();
        using var client = _fake.Client(Folder);
        await client.DownloadAsync();
        var found = TitleDbClient.Lookup(client.FilePath, [Zelda, Kirby.ToLowerInvariant(), Missing]);
        Assert.Equal(2, found.Count);
        Assert.Equal("Breath of the Wild", found[Zelda].Name); // not the demo listing
        Assert.Equal(20190412, found[Kirby].ReleaseDate);
        Assert.Throws<InvalidDataException>(() =>
        {
            System.IO.File.WriteAllText(client.FilePath, "[]");
            TitleDbClient.Lookup(client.FilePath, [Zelda]);
        });
    }

    /// <summary>Opt-in: reads a real TitleDB file (set SWITCHYARD_TITLEDB_FILE to its path) and times the scan.</summary>
    [Fact]
    public void Reads_a_real_titledb_file()
    {
        var file = Environment.GetEnvironmentVariable("SWITCHYARD_TITLEDB_FILE");
        if (string.IsNullOrEmpty(file)) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var found = TitleDbClient.Lookup(file, [Zelda, "0100225000FEE000"]);
        Assert.Contains("Breath of the Wild", found[Zelda].Name);
        Assert.Equal("Blaster Master Zero", found["0100225000FEE000"].Name);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
        Console.WriteLine($"Real TitleDB scan: {sw.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("01007EF00011E000", "01007EF00011E000")]
    [InlineData("01007ef00011e800", "01007EF00011E000")] // update
    [InlineData("0100", null)]
    [InlineData(null, null)]
    public void Base_title_id(string? id, string? expected) => Assert.Equal(expected, TitleDbService.BaseTitleId(id));

    [Fact]
    public async Task Images_come_only_from_nintendos_server()
    {
        using var client = _fake.Client(Folder);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadImageAsync("https://example.com/x.jpg"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadImageAsync("http://img-eshop.cdn.nintendo.net/i/x.jpg"));
    }

    // ------------------------------------------------------------- applying
    [Fact]
    public async Task Apply_saves_text_and_artwork_and_counts_the_rest()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var zelda = lib.AddEdition("Zelda BOTW", Generations.Switch1, _env.Game("Zelda.nsp"), consoleTitleId: Zelda);
        var kirby = lib.AddEdition("Kirby", Generations.Switch1, _env.Game("Kirby.nsp"), consoleTitleId: Kirby);
        lib.AddEdition("Homebrew", Generations.Switch1, _env.Game("Homebrew.nsp"), consoleTitleId: Missing);
        lib.AddEdition("No ID", Generations.Switch1, _env.Game("NoId.nsp"));
        using var client = _fake.Client(Folder);
        await client.DownloadAsync();
        var service = new TitleDbService(lib, client, ArtDir);

        var s = await service.ApplyAsync(lib.ListEditions());
        Assert.Equal(new TitleDbSummary(2, 2, 0, false), s);
        var m = lib.GetMetadata(zelda);
        Assert.Equal("Breath of the Wild", m.TdbName);
        Assert.Equal("About the game.", m.DisplaySummary);
        Assert.Equal(["Action", "Platformer"], m.DisplayGenres); // "Other" left out
        Assert.Equal(2019, m.DisplayReleaseYear);
        Assert.Equal("Example Publisher", m.TdbPublisher);
        Assert.Equal(2, m.TdbPlayers);
        Assert.True(System.IO.File.Exists(Path.Combine(ArtDir, m.TdbIconFile!)));
        Assert.Equal(m.TdbIconFile, m.DisplayCoverFile);
        Assert.Equal(m.TdbBannerFile, m.DisplayKeyArtFile);
        Assert.Equal(2, m.TdbArtUrls.Count); // banner + screenshot
        Assert.Null(lib.GetMetadata(kirby).TdbIconFile); // no icon listed
        Assert.True(lib.GetMetadata(kirby).HasTitleDb);

        // Running again downloads nothing new.
        var before = _fake.Requests.Count;
        await service.ApplyAsync(lib.ListEditions());
        Assert.Equal(before, _fake.Requests.Count);
    }

    [Fact]
    public async Task A_failed_image_is_counted_and_the_text_still_saved()
    {
        Seed();
        _fake.BrokenImages.Add("https://img-eshop.cdn.nintendo.net/i/icon-70010000000025.jpg");
        using var lib = _env.OpenLibrary();
        var zelda = lib.AddEdition("Zelda", Generations.Switch1, _env.Game(), consoleTitleId: Zelda);
        using var client = _fake.Client(Folder);
        await client.DownloadAsync();
        var s = await new TitleDbService(lib, client, ArtDir).ApplyAsync(lib.ListEditions());
        Assert.Equal(1, s.ImagesFailed);
        Assert.Equal("Breath of the Wild", lib.GetMetadata(zelda).TdbName);
        Assert.Null(lib.GetMetadata(zelda).TdbIconFile);
    }

    [Fact]
    public void Display_order_is_yours_then_titledb_then_igdb()
    {
        var m = EditionMetadata.Empty("x") with
        {
            Summary = "igdb", CoverFile = "igdb.jpg", KeyArtFile = "igdb-art.jpg", Genres = ["Puzzle"], ReleaseYear = 2001,
        };
        Assert.Equal("igdb", m.DisplaySummary);
        Assert.Equal("igdb.jpg", m.DisplayCoverFile);
        Assert.Equal(2001, m.DisplayReleaseYear);
        m = m with { TdbSummary = "eshop", TdbIconFile = "icon.jpg", TdbBannerFile = "banner.jpg", TdbGenres = ["Action"], TdbReleaseDate = 20200101 };
        Assert.Equal("eshop", m.DisplaySummary);
        Assert.Equal("icon.jpg", m.DisplayCoverFile);
        Assert.Equal("banner.jpg", m.DisplayKeyArtFile);
        Assert.Equal(["Action"], m.DisplayGenres);
        Assert.Equal(2020, m.DisplayReleaseYear);
        m = m with { UserSummary = "mine", UserCoverFile = "mine.png", UserKeyArtFile = "mine-art.png" };
        Assert.Equal("mine", m.DisplaySummary);
        Assert.Equal("mine.png", m.DisplayCoverFile);
        Assert.Equal("mine-art.png", m.DisplayKeyArtFile);
        Assert.Equal("eshop", m.DefaultSummary);
    }

    [Fact]
    public void Upgrade_from_v6_adds_titledb_columns()
    {
        string id;
        using (var lib = _env.OpenLibrary())
        {
            id = lib.AddEdition("Kept", Generations.Switch1, _env.Game());
            lib.SaveMetadata(EditionMetadata.Empty(id) with { IgdbName = "Kept" });
        }
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = string.Join(" ", new[] { "tdb_title_id", "tdb_name", "tdb_summary", "tdb_publisher", "tdb_release_date",
                    "tdb_genres_json", "tdb_players", "tdb_icon_file", "tdb_banner_file", "tdb_art_json", "tdb_fetched_at" }
                .Select(c => $"ALTER TABLE edition_metadata DROP COLUMN {c};")) + " UPDATE meta SET value = '6' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Equal("Kept", upgraded.GetMetadata(id).IgdbName);
        upgraded.SaveMetadata(upgraded.GetMetadata(id) with { TdbName = "Listed", TdbGenres = ["Action"] });
        Assert.Equal(["Action"], upgraded.GetMetadata(id).TdbGenres);
    }

    // ------------------------------------------------------------- app
    private MainViewModel Vm(LibraryStore lib, FakeUi ui, EmulatorLauncher launcher) =>
        new(lib, launcher, ui, _ => null, _ => null, () => _fake.Client(Folder));

    [AvaloniaFact]
    public async Task Update_from_titledb_fills_in_the_library()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var zelda = lib.AddEdition("Zelda BOTW", Generations.Switch1, _env.Game("Zelda.nsp"), consoleTitleId: Zelda);
        lib.AddEdition("Homebrew", Generations.Switch1, _env.Game("Homebrew.nsp"), consoleTitleId: Missing);
        using var launcher = new EmulatorLauncher();
        var vm = Vm(lib, new FakeUi(), launcher);

        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.Equal("TitleDB: 1 found, 1 not listed (filter: Not in TitleDB).", vm.StatusText);
        Assert.False(vm.IsMatchingAll);
        vm.Select(zelda);
        Assert.Equal("About the game.", vm.SelectedSummary);
        Assert.Contains("Example Publisher", vm.SelectedFactsLine);
        Assert.Contains("Up to 2 players", vm.SelectedFactsLine);
        Assert.Equal("TitleDB: Breath of the Wild", vm.MatchStatusText);
        Ui.WaitUntil(() => vm.SelectedCover is not null);

        vm.MoreFilter = vm.MoreFilters.First(f => f.Value == FilterOption.NotInTitleDbValue);
        Assert.Equal(["Homebrew"], vm.Items.Select(i => i.Title));
    }

    [AvaloniaFact]
    public async Task Offline_without_a_copy_explains_and_with_a_copy_carries_on()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Zelda", Generations.Switch1, _env.Game(), consoleTitleId: Zelda);
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Vm(lib, ui, launcher);
        _fake.Offline = true;
        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.Contains(ui.Messages, m => m.Contains("couldn't be downloaded"));

        _fake.Offline = false;
        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        _fake.Offline = true;
        await vm.UpdateFromTitleDbCommand.ExecuteAsync(null);
        Assert.StartsWith("TitleDB: 1 found", vm.StatusText); // used the copy already downloaded
    }

    [AvaloniaFact]
    public async Task Background_chooser_offers_the_eshop_banner_first()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var zelda = lib.AddEdition("Zelda", Generations.Switch1, _env.Game(), consoleTitleId: Zelda);
        using var client = _fake.Client(Folder);
        await client.DownloadAsync();
        await new TitleDbService(lib, client, ArtDir).ApplyAsync(lib.ListEditions());
        var sources = new ArtworkSources(lib, null, null, ArtDir, client);
        var choices = await sources.BackgroundCandidatesAsync(lib.GetEdition(zelda)!);
        Assert.Equal(["eShop banner", "eShop screenshot"], choices.Select(c => c.Source));
        Assert.NotEmpty(await choices[1].FetchFull(CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task Importing_offers_titledb_once_then_fills_in_new_games()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi { ConfirmResult = true };
        var vm = Vm(lib, ui, launcher);
        var folder = Path.Combine(_env.Root, "Import");
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(Path.Combine(folder, $"Zelda [{Zelda}][v0].nsp"), [1]);

        await vm.ImportFolderAsync(folder);
        Assert.Contains(ui.Messages, m => m.Contains("about 90 MB"));
        Assert.StartsWith("TitleDB: 1 found", vm.StatusText);

        ui.Messages.Clear();
        System.IO.File.WriteAllBytes(Path.Combine(folder, $"Kirby [{Kirby}][v0].nsp"), [1]);
        await vm.ImportFolderAsync(folder);
        Assert.DoesNotContain(ui.Messages, m => m.Contains("about 90 MB")); // already downloaded: no question
        Assert.Equal(2, lib.AllMetadata().Values.Count(m => m.HasTitleDb));
    }
}
