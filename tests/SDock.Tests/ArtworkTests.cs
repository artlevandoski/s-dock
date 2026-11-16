using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using SDock.App.ViewModels;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>Clear logos (SteamGridDB), choosing backgrounds and logos, and covers without the generation band.</summary>
public sealed class ArtworkTests : IDisposable
{
    private readonly TestEnv _env = new();
    private readonly FakeIgdb _igdb = new();
    private readonly FakeSteamGridDb _sgdb = new();
    public void Dispose() => _env.Dispose();

    private string ArtDir => Path.Combine(_env.Root, "artwork");

    private void Seed()
    {
        _igdb.Games.Add(FakeIgdb.Game(1003, "Haunted House", FakeIgdb.Switch1));
        _igdb.Games.Add(FakeIgdb.Game(1001, "Kirby's Return to Dream Land Deluxe", FakeIgdb.Switch1));
        _sgdb.Games.Add(new SgdbGame { Id = 77, Name = "Haunted House (fan remake)" });
        _sgdb.Games.Add(new SgdbGame { Id = 78, Name = "Haunted House", Verified = true });
        _sgdb.Games.Add(new SgdbGame { Id = 79, Name = "Kirby's Return to Dream Land Deluxe" });
        _sgdb.Logos[78] = [FakeSteamGridDb.Image(1, "logo", "white", score: 9), FakeSteamGridDb.Image(2, "logo", "official", score: 1)];
        _sgdb.Logos[79] = [FakeSteamGridDb.Image(3, "logo")];
        _sgdb.Heroes[78] = [FakeSteamGridDb.Image(10, "hero")];
    }

    private async Task<(LibraryStore Lib, string Id)> MatchedHauntedHouse()
    {
        Seed();
        var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        await new MetadataService(lib, _igdb.Client(), ArtDir).AutoMatchAsync(lib.GetEdition(id)!);
        return (lib, id);
    }

    private ArtworkSources Sources(LibraryStore lib, bool withSgdb = true) =>
        new(lib, _igdb.Client(), withSgdb ? _sgdb.Client() : null, ArtDir);

    // ---------------------------------------------------------- SteamGridDB
    [Fact]
    public async Task SteamGridDb_finds_the_exact_game_and_ranks_official_logos_first()
    {
        Seed();
        using var client = _sgdb.Client();
        Assert.Equal(78, (await client.FindGameAsync("Haunted House"))!.Id);
        Assert.Null(await client.FindGameAsync("Nothing Like It"));
        Assert.Equal([2L, 1L], (await client.LogosAsync(78)).Select(l => l.Id));
        Assert.All(_sgdb.AuthHeaders, h => Assert.Equal("Bearer test-sgdb-key", h));

        var ranked = SteamGridDbClient.Rank([
            FakeSteamGridDb.Image(1, "logo", "custom", score: 50, language: "ja"),
            FakeSteamGridDb.Image(2, "logo", "custom", score: 5),
            FakeSteamGridDb.Image(3, "logo", "official", score: 0)]);
        Assert.Equal([3L, 2L, 1L], ranked.Select(r => r.Id));
    }

    [Fact]
    public async Task SteamGridDb_bad_key_and_foreign_image_hosts_are_refused()
    {
        _sgdb.RejectKey = true;
        using var client = _sgdb.Client();
        Assert.Contains("API key", (await Assert.ThrowsAsync<IgdbException>(() => client.TestAsync())).Message);
        await Assert.ThrowsAsync<IgdbException>(() => client.DownloadAsync("https://evil.example.com/logo.png"));
        await Assert.ThrowsAsync<IgdbException>(() => client.DownloadAsync("http://cdn2.steamgriddb.com/logo/1.png"));
    }

    [Fact]
    public async Task Fetches_a_clear_logo_once_and_keeps_the_users_own()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        var sources = Sources(lib);

        Assert.True(await sources.FetchLogoAsync(lib.GetEdition(id)!));
        var m = lib.GetMetadata(id);
        Assert.Equal(78, m.SteamGridDbId);
        Assert.True(File.Exists(Path.Combine(ArtDir, m.LogoFile!)));
        Assert.Equal(m.LogoFile, m.DisplayLogoFile);
        Assert.Contains(_sgdb.Requests, r => r.EndsWith("/logo/2.png")); // the official one

        Assert.False(await sources.FetchLogoAsync(lib.GetEdition(id)!)); // already cached

        // The user's logo wins and survives a replacement fetch.
        var mine = Path.Combine(_env.Root, "my logo.png");
        File.WriteAllBytes(mine, FakeIgdb.TinyPng);
        UserMetadata.SetArt(lib, ArtDir, id, ArtKind.Logo, mine);
        Assert.True(await sources.FetchLogoAsync(lib.GetEdition(id)!, replace: true));
        m = lib.GetMetadata(id);
        Assert.Equal(m.UserLogoFile, m.DisplayLogoFile);
    }

    // ------------------------------------------------------------ IGDB logos
    [Fact]
    public async Task Igdb_logo_is_used_first_as_a_transparent_png()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        _igdb.Logos[1003] =
        [
            FakeIgdb.Logo(5, transparent: false, width: 2000, height: 800), // bigger, but not transparent
            FakeIgdb.Logo(6, transparent: true, type: "Logo - Light"),
        ];

        Assert.True(await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!));
        var q = _igdb.Queries.Last(x => x.Endpoint == "logos");
        Assert.Contains("where game = 1003", q.Body);
        Assert.Contains(_igdb.ImageRequests, u => u.EndsWith("t_1080p/lo6.png")); // transparent first, as PNG
        Assert.DoesNotContain(_sgdb.Requests, r => r.Contains("/logo/"));        // SteamGridDB not needed
        Assert.EndsWith(".png", lib.GetMetadata(id).LogoFile);

        var candidates = await Sources(lib).LogoCandidatesAsync(lib.GetEdition(id)!);
        Assert.Equal(["IGDB logo (Logo - Light)", "IGDB logo", "SteamGridDB logo (official)", "SteamGridDB logo (white)"],
            candidates.Select(c => c.Source));
    }

    [Fact]
    public async Task Igdb_logos_work_without_a_steamgriddb_key()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        _igdb.Logos[1003] = [FakeIgdb.Logo(6)];
        Assert.True(await Sources(lib, withSgdb: false).FetchLogoAsync(lib.GetEdition(id)!));
        Assert.NotNull(lib.GetMetadata(id).DisplayLogoFile);
    }

    [Fact]
    public async Task Falls_back_to_steamgriddb_when_igdb_has_no_logo_or_no_logos_endpoint()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        Assert.True(await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!)); // IGDB: none for this game
        Assert.Contains(_sgdb.Requests, r => r.EndsWith("/logo/2.png"));

        _igdb.NoLogosEndpoint = true;
        Assert.True(await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!, replace: true));
        Assert.Single(Directory.GetFiles(Path.Combine(ArtDir, id), "logo.*")); // replaced, not duplicated
    }

    [AvaloniaFact]
    public async Task Matching_adds_the_igdb_logo_without_steamgriddb()
    {
        Seed();
        _igdb.Logos[1003] = [FakeIgdb.Logo(6)];
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = Window(lib, new FakeUi(), launcher, withSgdb: false);
        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        Ui.WaitUntil(() => vm.HasLogo);
        Assert.Contains("1 logo(s)", vm.StatusText);
    }

    [Fact]
    public async Task No_key_or_no_logo_means_no_logo()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        Assert.False(await Sources(lib, withSgdb: false).FetchLogoAsync(lib.GetEdition(id)!));
        _sgdb.Logos.Clear();
        Assert.False(await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!));
        Assert.Null(lib.GetMetadata(id).DisplayLogoFile);
    }

    [Fact]
    public async Task Changing_the_match_drops_the_old_games_logo()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!);
        var oldLogo = Path.Combine(ArtDir, lib.GetMetadata(id).LogoFile!);

        await new MetadataService(lib, _igdb.Client(), ArtDir).ApplyMatchAsync(id, _igdb.Games.Single(g => g.Id == 1001));
        var m = lib.GetMetadata(id);
        Assert.Null(m.LogoFile);
        Assert.Null(m.SteamGridDbId);
        Assert.False(File.Exists(oldLogo));
        Assert.True(await Sources(lib).FetchLogoAsync(lib.GetEdition(id)!));
        Assert.Equal(79, lib.GetMetadata(id).SteamGridDbId);
    }

    // ------------------------------------------------------------ choosers
    [Fact]
    public async Task Background_candidates_and_choice_kept_on_refresh()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        var sources = Sources(lib);
        var candidates = await sources.BackgroundCandidatesAsync(lib.GetEdition(id)!);
        Assert.Equal(["IGDB artwork", "IGDB screenshot", "IGDB screenshot", "SteamGridDB hero (official)"],
            candidates.Select(c => c.Source));

        await sources.UseAsync(id, ArtKind.KeyArt, candidates[2]); // the second screenshot
        Assert.Contains(_igdb.ImageRequests, u => u.EndsWith("t_1080p/sc1003b.jpg"));
        var chosen = lib.GetMetadata(id).UserKeyArtFile;
        Assert.NotNull(chosen);

        await new MetadataService(lib, _igdb.Client(), ArtDir).RefreshAsync(lib.GetEdition(id)!);
        Assert.Equal(chosen, lib.GetMetadata(id).DisplayKeyArtFile);
    }

    [Fact]
    public async Task Logo_candidates_come_from_steamgriddb()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        var logos = await Sources(lib).LogoCandidatesAsync(lib.GetEdition(id)!);
        Assert.Equal(["SteamGridDB logo (official)", "SteamGridDB logo (white)"], logos.Select(l => l.Source));
        Assert.Equal(".png", logos[0].Extension);
        Assert.Empty(await Sources(lib, withSgdb: false).LogoCandidatesAsync(lib.GetEdition(id)!));
    }

    [Fact]
    public void Upgrade_from_v4_adds_logo_columns()
    {
        string x;
        using (var lib = _env.OpenLibrary())
        {
            x = lib.AddEdition("Game", Generations.Switch1, _env.Game());
            lib.SaveMetadata(EditionMetadata.Empty(x) with { IgdbName = "Kept" });
        }
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Rebuild the v4 table without the new columns, keeping the row.
            cmd.CommandText = """
                PRAGMA foreign_keys = OFF;
                CREATE TABLE m4 (
                    edition_id TEXT PRIMARY KEY REFERENCES editions(id) ON DELETE CASCADE,
                    igdb_name TEXT, igdb_slug TEXT, igdb_url TEXT, summary TEXT, genres_json TEXT, release_year INTEGER,
                    cover_file TEXT, keyart_file TEXT, fetched_at TEXT, user_summary TEXT, user_cover_file TEXT,
                    user_keyart_file TEXT, user_logo_file TEXT);
                INSERT INTO m4 SELECT edition_id, igdb_name, igdb_slug, igdb_url, summary, genres_json, release_year,
                    cover_file, keyart_file, fetched_at, user_summary, user_cover_file, user_keyart_file, user_logo_file
                    FROM edition_metadata;
                DROP TABLE edition_metadata;
                ALTER TABLE m4 RENAME TO edition_metadata;
                UPDATE meta SET value = '4' WHERE key = 'schema_version';
                """;
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Equal("Kept", upgraded.GetMetadata(x).IgdbName);
        upgraded.SaveMetadata(upgraded.GetMetadata(x) with { LogoFile = "x/logo.png", SteamGridDbId = 5 });
        Assert.Equal(5, upgraded.GetMetadata(x).SteamGridDbId);
    }

    // ------------------------------------------------------------------- UI
    private MainViewModel Window(LibraryStore lib, FakeUi ui, EmulatorLauncher launcher, bool withSgdb = true)
    {
        lib.IgdbCredentials = ("id", "secret");
        if (withSgdb) lib.SteamGridDbKey = "test-sgdb-key";
        return new MainViewModel(lib, launcher, ui, _ => _igdb.Client(), l => l.HasSteamGridDbKey ? _sgdb.Client() : null);
    }

    [AvaloniaFact]
    public async Task Get_missing_logos_fills_matched_games_and_reports()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        var service = new MetadataService(lib, _igdb.Client(), ArtDir);
        foreach (var title in new[] { "Haunted House", "Kirby's Return to Dream Land Deluxe" })
            await service.AutoMatchAsync(lib.GetEdition(lib.AddEdition(title, Generations.Switch1, _env.Game(title + ".nsp")))!);
        lib.AddEdition("Unmatched Homebrew", Generations.Switch1, _env.Game("Homebrew.nsp")); // not matched: skipped
        using var launcher = new EmulatorLauncher();
        var vm = Window(lib, new FakeUi(), launcher);

        await vm.GetMissingLogosCommand.ExecuteAsync(null);
        Assert.Equal("Logos: 2 added.", vm.StatusText);
        Assert.False(vm.IsMatchingAll);
        await vm.GetMissingLogosCommand.ExecuteAsync(null);
        Assert.Equal("Every matched game already has a logo.", vm.StatusText);
    }

    [Fact]
    public async Task Match_all_fetches_each_logo_right_after_its_match()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game("Haunted House.nsp"));
        lib.AddEdition("Kirby's Return to Dream Land Deluxe", Generations.Switch1, _env.Game("Kirby.nsp"));
        var service = new MetadataService(lib, _igdb.Client(), ArtDir);
        var matchedSoFar = new List<int>();
        var after = new List<string>();
        await service.MatchAllAsync(lib.ListEditions(), afterMatch: (id, _) =>
        {
            after.Add(id);
            matchedSoFar.Add(lib.ListEditions().Count(e => e.IgdbId is not null));
            return Task.CompletedTask;
        });
        Assert.Equal(2, after.Count);
        Assert.Equal([1, 2], matchedSoFar); // called as each game is matched, not once at the end
    }

    [AvaloniaFact]
    public async Task Matching_adds_the_clear_logo_in_place_of_the_name()
    {
        Seed();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = Window(lib, new FakeUi(), launcher);
        Assert.False(vm.HasLogo);

        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        Assert.Contains("1 logo(s)", vm.StatusText);
        Ui.WaitUntil(() => vm.HasLogo);
        Ui.WaitUntil(() => vm.SelectedLogo is not null);
    }

    [AvaloniaFact]
    public async Task Choose_background_and_logo_then_use_default()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher);
        vm.Select(id);

        ui.OnArtPicker = p =>
        {
            Assert.False(p.IsLogo);
            Assert.Equal(4, p.Choices.Count);
            Assert.All(p.Choices, c => Assert.NotNull(c.Thumbnail));
            return new ArtPick(Candidate: p.Choices[3].Candidate); // the SteamGridDB hero
        };
        await vm.ChooseBackgroundCommand.ExecuteAsync(null);
        Assert.NotNull(lib.GetMetadata(id).UserKeyArtFile);
        Ui.WaitUntil(() => vm.SelectedKeyArt is not null);
        Assert.Contains("Background updated", vm.StatusText);

        ui.OnArtPicker = p =>
        {
            Assert.True(p.IsLogo);
            return new ArtPick(Candidate: p.Choices.Single(c => c.Detail.Contains("white")).Candidate);
        };
        await vm.ChooseLogoCommand.ExecuteAsync(null);
        Ui.WaitUntil(() => vm.HasLogo);
        Assert.NotNull(lib.GetMetadata(id).UserLogoFile);

        ui.OnArtPicker = _ => new ArtPick(UseDefault: true);
        await vm.ChooseLogoCommand.ExecuteAsync(null);
        Assert.Null(lib.GetMetadata(id).UserLogoFile);
    }

    [AvaloniaFact]
    public async Task Logo_chooser_explains_how_to_get_logos_without_a_key()
    {
        var (lib, id) = await MatchedHauntedHouse();
        using var _ = lib;
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher, withSgdb: false);
        vm.Select(id);
        ui.OnArtPicker = p =>
        {
            Assert.True(p.IsEmpty);
            Assert.Contains("SteamGridDB API key", p.Status);
            return null;
        };
        await vm.ChooseLogoCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task Settings_test_steamgriddb_key()
    {
        var vm = new IgdbSettingsViewModel("", "", new FakeUi(), (_, _) => _igdb.Client())
        {
            SteamGridDbKey = "k",
            SteamGridDbFactory = _ => _sgdb.Client(),
        };
        await vm.TestSteamGridDbCommand.ExecuteAsync(null);
        Assert.False(vm.SteamGridDbStatusIsError);
        _sgdb.RejectKey = true;
        await vm.TestSteamGridDbCommand.ExecuteAsync(null);
        Assert.True(vm.SteamGridDbStatusIsError);
    }

    [AvaloniaFact]
    public async Task Renders_logo_in_place_of_name_and_the_choosers()
    {
        Seed();
        _igdb.ImageBytes = MetadataTests.FakeArt;
        _sgdb.ImageBytes = MetadataTests.FakeArt;
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game("Haunted House.nsp"));
        lib.AddEdition("Kirby's Return to Dream Land Deluxe", Generations.Switch1, _env.Game("Kirby.nsp"));
        lib.AddEdition("Unmatched Homebrew", Generations.Switch1, _env.Game("Homebrew.nsp"));
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher);
        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        vm.Select(id);
        var sources = new ArtworkSources(lib, _igdb.Client(), _sgdb.Client(), SDock.Core.AppPaths.ArtworkDir);
        await sources.UseAsync(id, ArtKind.KeyArt, (await sources.BackgroundCandidatesAsync(lib.GetEdition(id)!))[3]);
        vm.Refresh(id);

        var window = new SDock.App.Views.MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        MainWindowTests.Capture(window, "m4b-grid.png");
        var expander = window.GetVisualDescendants().OfType<Avalonia.Controls.Expander>().First();
        expander.IsExpanded = true;
        MainWindowTests.Capture(window, "m4b-grid-settings.png");
        expander.IsExpanded = false;
        vm.IsCarousel = true;
        window.Width = 1920; // full screen on a 1080p display (the headless window doesn't resize itself)
        window.Height = 1080;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 30; i++) // let the selected cover's scale animation finish
        {
            Thread.Sleep(16);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        window.CenterCarousel(animate: false);
        MainWindowTests.Capture(window, "m4b-carousel.png");
        vm.BackgroundIsColor = true;
        MainWindowTests.Capture(window, "carousel-color.png");
        vm.BackgroundIsArt = true;
        vm.OpenGameMenu();
        MainWindowTests.Capture(window, "menu-game.png");
        vm.CloseMenu();
        vm.OpenSettings();
        MainWindowTests.Capture(window, "settings-page.png");
        vm.SettingsInput(SDock.App.Input.PadAction.Down);
        vm.SettingsInput(SDock.App.Input.PadAction.Accept);
        MainWindowTests.Capture(window, "settings-emulators.png");
        vm.CloseSettings();
        window.Close();

        var bg = new ArtPickerViewModel(ArtKind.KeyArt, "Haunted House", true, ui,
            ct => sources.BackgroundCandidatesAsync(lib.GetEdition(id)!, ct));
        var bgWindow = new SDock.App.Views.ArtPickerWindow(bg);
        bgWindow.Show();
        await bg.StartAsync();
        Assert.Equal(4, bg.Choices.Count); // loaded once, though both the window and the test start it
        MainWindowTests.Capture(bgWindow, "m4b-choose-background.png");
        bgWindow.Close();

        var logo = new ArtPickerViewModel(ArtKind.Logo, "Haunted House", true, ui,
            ct => sources.LogoCandidatesAsync(lib.GetEdition(id)!, ct));
        var logoWindow = new SDock.App.Views.ArtPickerWindow(logo);
        logoWindow.Show();
        await logo.StartAsync();
        MainWindowTests.Capture(logoWindow, "m4b-choose-logo.png");
        logoWindow.Close();

        var settings = new SDock.App.Views.IgdbSettingsWindow(new IgdbSettingsViewModel("", "", ui, (_, _) => _igdb.Client()));
        settings.Show();
        MainWindowTests.Capture(settings, "m4b-settings.png");
        settings.Close();
    }

    [Fact]
    public void Game_selector_covers_have_no_generation_band()
    {
        var xaml = File.ReadAllText(Path.Combine(AppPaths.FindSourceRoot(AppContext.BaseDirectory)!,
            "src", "SDock.App", "Views", "MainWindow.axaml"));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(xaml, "ShowGenerationBand=\"False\"").Count); // grid, carousel, details
    }
}
