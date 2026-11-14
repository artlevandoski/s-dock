using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using SwitchYard.App.ViewModels;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.Tests;

/// <summary>Milestone 4: IGDB metadata and artwork, against a fake IGDB.</summary>
public sealed class MetadataTests : IDisposable
{
    private readonly TestEnv _env = new();
    private readonly FakeIgdb _igdb = new();
    public void Dispose() => _env.Dispose();

    private string ArtDir => Path.Combine(_env.Root, "artwork");

    private MetadataService Service(LibraryStore lib) => new(lib, _igdb.Client(), ArtDir);

    private void SeedGames()
    {
        _igdb.Games.Add(FakeIgdb.Game(1001, "Kirby's Return to Dream Land Deluxe", FakeIgdb.Switch1));
        _igdb.Games.Add(FakeIgdb.Game(1002, "Haunted House", FakeIgdb.Pc));                 // a different game, PC only
        _igdb.Games.Add(FakeIgdb.Game(1003, "Haunted House", FakeIgdb.Switch1));            // the Switch one
        _igdb.Games.Add(FakeIgdb.Game(1004, "Puzzle Quest", FakeIgdb.Switch1));
        _igdb.Games.Add(FakeIgdb.Game(1005, "Puzzle Quest", FakeIgdb.Switch1, FakeIgdb.Pc)); // two on Switch: ambiguous
        _igdb.Games.Add(FakeIgdb.Game(1006, "Arcade Archives: Donkey Kong", FakeIgdb.Switch1));
        _igdb.Games.Add(FakeIgdb.Game(1007, "Mario Kart World", FakeIgdb.Switch2));
    }

    // ----------------------------------------------------------------- client
    [Fact]
    public async Task Signs_in_once_and_sends_credentials_with_each_request()
    {
        SeedGames();
        using var client = _igdb.Client();
        await client.SearchAsync("Kirby", [FakeIgdb.Switch1Platform]);
        await client.SearchAsync("Haunted House", [FakeIgdb.Switch1Platform]);
        Assert.Equal(1, _igdb.TokenRequests);
        Assert.All(_igdb.Queries, q =>
        {
            Assert.Equal("test-client-id", q.ClientId);
            Assert.Equal("token-1", q.Bearer);
        });
        Assert.Contains("where platforms = (130)", _igdb.Queries[0].Body);
    }

    [Fact]
    public async Task Expired_token_is_renewed_once()
    {
        SeedGames();
        _igdb.UnauthorizedResponsesLeft = 1;
        using var client = _igdb.Client();
        Assert.NotNull(await client.GetByIdAsync(1001));
        Assert.Equal(2, _igdb.TokenRequests);
    }

    [Fact]
    public async Task Bad_credentials_give_a_clear_message()
    {
        _igdb.RejectCredentials = true;
        using var client = _igdb.Client();
        var ex = await Assert.ThrowsAsync<IgdbException>(() => client.TestAsync());
        Assert.Contains("Client ID and Client Secret", ex.Message);
    }

    [Theory]
    [InlineData("1234", 1234L, null)]
    [InlineData("https://www.igdb.com/games/kirbys-return-to-dream-land-deluxe", null, "kirbys-return-to-dream-land-deluxe")]
    [InlineData("igdb.com/games/haunted-house--1", null, "haunted-house--1")]
    [InlineData("haunted-house", null, "haunted-house")]
    [InlineData("not a link!", null, null)]
    [InlineData("", null, null)]
    public void Parses_pasted_links_and_ids(string text, long? id, string? slug) =>
        Assert.Equal((id, slug), IgdbClient.ParseReference(text));

    [Fact]
    public void Search_text_cannot_break_out_of_the_query() =>
        Assert.Equal("Bad   Title", IgdbClient.Escape("Bad \" Title"));

    // --------------------------------------------------------------- matching
    [Fact]
    public void Confident_match_rules()
    {
        IReadOnlyList<long> s1 = [FakeIgdb.Switch1Platform];
        var kirby = FakeIgdb.Game(1, "Kirby's Return to Dream Land Deluxe", FakeIgdb.Switch1);
        Assert.Equal(kirby, MetadataService.PickConfident("Kirby’s Return to Dream Land™ Deluxe", s1, [kirby]));
        Assert.Equal(kirby, MetadataService.PickConfident("KIRBYS RETURN TO DREAM LAND DELUXE", s1, [kirby]));

        var pc = FakeIgdb.Game(2, "Haunted House", FakeIgdb.Pc);
        var sw = FakeIgdb.Game(3, "Haunted House", FakeIgdb.Switch1);
        Assert.Equal(sw, MetadataService.PickConfident("Haunted House", s1, [pc, sw])); // platform decides

        var a = FakeIgdb.Game(4, "Puzzle Quest", FakeIgdb.Switch1);
        var b = FakeIgdb.Game(5, "Puzzle Quest", FakeIgdb.Switch1);
        Assert.Null(MetadataService.PickConfident("Puzzle Quest", s1, [a, b]));          // still ambiguous
        Assert.Null(MetadataService.PickConfident("Puzzle Quest 2", s1, [a]));            // not the same title
        Assert.Equal("arcadearchivesdonkeykong", MetadataService.MatchKey("Arcade Archives: Donkey Kong"));
        Assert.Equal(MetadataService.MatchKey("The Game & Watch"), MetadataService.MatchKey("Game and Watch"));
    }

    [Fact]
    public async Task Auto_match_saves_metadata_and_caches_artwork()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Kirby’s Return to Dream Land™ Deluxe", Generations.Switch1, _env.Game());
        var outcome = await Service(lib).AutoMatchAsync(lib.GetEdition(id)!);

        Assert.Equal(MatchOutcome.Matched, outcome);
        Assert.Equal(1001, lib.GetEdition(id)!.IgdbId);
        var m = lib.GetMetadata(id);
        Assert.Equal("Kirby's Return to Dream Land Deluxe", m.IgdbName);
        Assert.Equal("Kirby's Return to Dream Land Deluxe summary from IGDB.", m.DisplaySummary);
        Assert.Equal(["Platform", "Adventure"], m.Genres);
        Assert.Equal(2023, m.ReleaseYear);
        Assert.True(File.Exists(Path.Combine(ArtDir, m.CoverFile!)));
        Assert.True(File.Exists(Path.Combine(ArtDir, m.KeyArtFile!)));
        Assert.Contains(_igdb.ImageRequests, u => u.Contains("t_cover_big_2x/co1001.jpg"));
        Assert.Contains(_igdb.ImageRequests, u => u.Contains("t_1080p/ar1001.jpg"));
    }

    [Fact]
    public async Task Platform_aware_matching_by_generation()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var haunted = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game("Haunted House.nsp"));
        var kart = lib.AddEdition("Mario Kart World", Generations.Switch1, _env.Game("Mario Kart World.nsp")); // wrong generation
        var kartS2 = lib.AddEdition("Mario Kart World", Generations.Switch2, _env.Game("MKW S2.nsp"));
        var unclassified = lib.AddEdition("Mario Kart World", Generations.Unclassified, _env.Game("MKW U.nsp"));
        var service = Service(lib);

        Assert.Equal(MatchOutcome.Matched, await service.AutoMatchAsync(lib.GetEdition(haunted)!));
        Assert.Equal(1003, lib.GetEdition(haunted)!.IgdbId); // the Switch game, not the PC one
        Assert.Equal(MatchOutcome.NotFound, await service.AutoMatchAsync(lib.GetEdition(kart)!));
        Assert.Equal(MatchOutcome.Matched, await service.AutoMatchAsync(lib.GetEdition(kartS2)!));
        Assert.Equal(MatchOutcome.Matched, await service.AutoMatchAsync(lib.GetEdition(unclassified)!)); // searches both
        Assert.Contains(_igdb.Queries, q => q.Body.Contains("where platforms = (508)"));
        Assert.Contains(_igdb.Queries, q => q.Body.Contains("where platforms = (130,508)"));
    }

    [Fact]
    public async Task Match_all_reports_what_needs_review_and_never_rematches()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        foreach (var t in new[] { "Kirby's Return to Dream Land Deluxe", "Puzzle Quest", "Unknown Homebrew Thing", "Arcade Archives DONKEY KONG" })
            lib.AddEdition(t, Generations.Switch1, _env.Game(t + ".nsp"));
        var progress = new List<string>();
        var summary = await Service(lib).MatchAllAsync(lib.ListEditions(), new SyncProgress(p => progress.Add(p.Title)));

        Assert.Equal(new MatchAllSummary(Matched: 2, NeedsMatch: 1, NotFound: 1, Failed: 0, Cancelled: false), summary);
        Assert.Contains("Puzzle Quest", progress);
        var queries = _igdb.Queries.Count;
        var again = await Service(lib).MatchAllAsync(lib.ListEditions());
        Assert.Equal(0, again.Matched);
        Assert.Equal(queries + 2 + 1, _igdb.Queries.Count); // only the two unmatched were searched (+1 platform lookup)
    }

    [Fact]
    public async Task Match_all_stops_on_bad_credentials_and_can_be_cancelled()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Kirby's Return to Dream Land Deluxe", Generations.Switch1, _env.Game());
        _igdb.RejectCredentials = true;
        await Assert.ThrowsAsync<IgdbException>(() => Service(lib).MatchAllAsync(lib.ListEditions()));

        _igdb.RejectCredentials = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.True((await Service(lib).MatchAllAsync(lib.ListEditions(), ct: cts.Token)).Cancelled);
    }

    [Fact]
    public async Task Refresh_keeps_the_users_text_and_art()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        var service = Service(lib);
        await service.AutoMatchAsync(lib.GetEdition(id)!);

        var myCover = Path.Combine(_env.Root, "my cover.png");
        File.WriteAllBytes(myCover, FakeIgdb.TinyPng);
        UserMetadata.SetSummary(lib, id, "My own notes about this game.");
        UserMetadata.SetArt(lib, ArtDir, id, ArtKind.Cover, myCover);
        UserMetadata.SetArt(lib, ArtDir, id, ArtKind.Logo, myCover);

        _igdb.Games[2] = _igdb.Games[2] with { Summary = "Updated IGDB summary." };
        Assert.True(await service.RefreshAsync(lib.GetEdition(id)!));
        var m = lib.GetMetadata(id);
        Assert.Equal("Updated IGDB summary.", m.Summary);
        Assert.Equal("My own notes about this game.", m.DisplaySummary);
        Assert.Equal(m.UserCoverFile, m.DisplayCoverFile);
        Assert.NotNull(m.DisplayLogoFile);

        // Clearing the user's text goes back to IGDB's; resetting art shows IGDB's cover again.
        UserMetadata.SetSummary(lib, id, "  ");
        UserMetadata.ResetArt(lib, ArtDir, id, ArtKind.Cover);
        m = lib.GetMetadata(id);
        Assert.Equal("Updated IGDB summary.", m.DisplaySummary);
        Assert.Equal(m.CoverFile, m.DisplayCoverFile);
        Assert.Throws<InvalidOperationException>(() => UserMetadata.SetArt(lib, ArtDir, id, ArtKind.Cover, _env.Game("not an image.nsp")));
    }

    [Fact]
    public async Task Remove_match_clears_igdb_data_but_keeps_user_edits()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        await Service(lib).AutoMatchAsync(lib.GetEdition(id)!);
        UserMetadata.SetSummary(lib, id, "Mine.");
        var cached = Path.Combine(ArtDir, lib.GetMetadata(id).CoverFile!);

        MetadataService.RemoveMatch(lib, ArtDir, id);
        var m = lib.GetMetadata(id);
        Assert.Null(lib.GetEdition(id)!.IgdbId);
        Assert.Null(m.IgdbName);
        Assert.Null(m.CoverFile);
        Assert.False(File.Exists(cached));
        Assert.Equal("Mine.", m.DisplaySummary);
    }

    [Fact]
    public void Credentials_live_only_in_the_local_database()
    {
        using (var lib = _env.OpenLibrary())
        {
            Assert.False(lib.HasIgdbCredentials);
            lib.IgdbCredentials = (" my-id ", " my-secret ");
        }
        using var reopened = _env.OpenLibrary();
        Assert.Equal(("my-id", "my-secret"), reopened.IgdbCredentials);
        Assert.True(reopened.HasIgdbCredentials);
        var repo = AppPaths.FindSourceRoot(AppContext.BaseDirectory)!;
        Assert.Contains("/data/", File.ReadAllText(Path.Combine(repo, ".gitignore")));
    }

    [Fact]
    public void Upgrade_from_v3_adds_metadata_table()
    {
        using (var lib = _env.OpenLibrary()) { }
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE edition_metadata; UPDATE meta SET value = '3' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Empty(upgraded.AllMetadata());
    }

    // --------------------------------------------------------------------- UI
    private MainViewModel Window(LibraryStore lib, FakeUi ui, EmulatorLauncher launcher)
    {
        lib.IgdbCredentials = ("test-client-id", "test-secret");
        return new MainViewModel(lib, launcher, ui, _ => _igdb.Client());
    }

    [AvaloniaFact]
    public async Task Match_unmatched_then_needs_match_filter_shows_the_rest()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Kirby's Return to Dream Land Deluxe", Generations.Switch1, _env.Game("k.nsp"));
        lib.AddEdition("Puzzle Quest", Generations.Switch1, _env.Game("p.nsp"));
        using var launcher = new EmulatorLauncher();
        var vm = Window(lib, new FakeUi(), launcher);

        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        Assert.Contains("1 matched", vm.StatusText);
        Assert.Contains("1 need a manual match", vm.StatusText);

        var kirby = vm.Items.Single(i => i.Title.StartsWith("Kirby"));
        Assert.True(kirby.IsMatched);
        Assert.NotNull(kirby.Cover); // real artwork instead of the placeholder

        vm.GenerationFilter = FilterOption.Generation.Single(o => o.Value == FilterOption.NeedsMatchValue);
        Assert.Equal("Puzzle Quest", Assert.Single(vm.Items).Title);
    }

    [AvaloniaFact]
    public async Task Match_dialog_search_lookup_and_change_match()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Puzzle Quest", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher);
        vm.Select(id);
        Assert.Equal("Match on IGDB…", vm.MatchMenuLabel);
        Assert.Equal("Not matched on IGDB", vm.MatchStatusText);

        ui.OnMatch = m =>
        {
            Assert.Equal(2, m.Results.Count);
            Assert.All(m.Results, r => Assert.NotNull(r.Thumbnail)); // previews
            return m.Results.Single(r => r.Game.Id == 1005).Game;
        };
        await vm.MatchOnIgdbCommand.ExecuteAsync(null);
        Assert.Equal(1005, lib.GetEdition(id)!.IgdbId);
        Assert.Equal("Change Match…", vm.MatchMenuLabel);
        Assert.Equal("IGDB: Puzzle Quest", vm.MatchStatusText);
        Assert.Equal("Platform  ·  Adventure  ·  2023", vm.SelectedFactsLine);

        // Change Match via a pasted IGDB link.
        ui.OnMatch = m =>
        {
            Assert.True(m.Results.Single(r => r.Game.Id == 1005).IsCurrent);
            m.Reference = "https://www.igdb.com/games/haunted-house";
            m.LookUpCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            return m.Selected?.Game;
        };
        await vm.MatchOnIgdbCommand.ExecuteAsync(null);
        Assert.Contains(lib.GetEdition(id)!.IgdbId, new long?[] { 1002, 1003 });
    }

    [AvaloniaFact]
    public async Task Match_dialog_all_platforms_toggle()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Mario Kart World", Generations.Switch1, _env.Game());
        var m = new MatchViewModel(lib.GetEdition(id)!, new MetadataService(lib, _igdb.Client(), ArtDir));
        await m.StartAsync();
        Assert.Empty(m.Results);
        Assert.Contains("Search all platforms", m.Status);
        m.AnyPlatform = true; // re-searches
        await m.SearchCommand.ExecuteAsync(null);
        Assert.Contains("not listed for this generation", Assert.Single(m.Results).Detail);
    }

    [AvaloniaFact]
    public async Task Edit_description_and_set_cover_from_details()
    {
        SeedGames();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher);
        vm.Select(id);
        Assert.Null(vm.SelectedCover);

        ui.NextEditedText = "A spooky classic.";
        await vm.EditDescriptionCommand.ExecuteAsync(null);
        Assert.Equal("A spooky classic.", vm.SelectedSummary);

        var png = Path.Combine(_env.Root, "cover.png");
        File.WriteAllBytes(png, FakeIgdb.TinyPng);
        ui.NextPickedFile = png;
        await vm.SetCoverCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedCover);
        Assert.NotNull(lib.GetMetadata(id).UserCoverFile);

        vm.ResetArtworkCommand.Execute(null);
        Assert.Null(vm.SelectedCover);
    }

    [AvaloniaFact]
    public async Task No_credentials_offers_igdb_settings()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Game", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, launcher, ui, l => l.HasIgdbCredentials ? _igdb.Client() : null);
        ui.ConfirmResult = true;
        ui.OnIgdbSettings = s =>
        {
            s.ClientId = "pasted-id";
            s.ClientSecret = "pasted-secret";
            return true;
        };
        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        Assert.Contains(ui.Messages, m => m.Contains("free IGDB (Twitch)"));
        Assert.Equal(("pasted-id", "pasted-secret"), lib.IgdbCredentials);
    }

    [AvaloniaFact]
    public async Task Igdb_settings_test_connection()
    {
        var ui = new FakeUi();
        var vm = new IgdbSettingsViewModel("id", "secret", ui, (_, _) => _igdb.Client());
        await vm.TestCommand.ExecuteAsync(null);
        Assert.False(vm.StatusIsError);
        Assert.Contains("Connected", vm.Status);

        _igdb.RejectCredentials = true;
        await vm.TestCommand.ExecuteAsync(null);
        Assert.True(vm.StatusIsError);

        vm.OpenTwitchConsoleCommand.Execute(null);
        Assert.Equal([IgdbSettingsViewModel.TwitchConsoleUrl], ui.OpenedUrls);
    }

    /// <summary>Renders a stand-in artwork image (portrait cover or landscape key art) as PNG bytes.</summary>
    internal static byte[] FakeArt(string url)
    {
        var isLogo = url.Contains("/logo");
        var landscape = url.Contains("t_1080p") || url.Contains("screenshot") || url.Contains("/hero");
        var (w, h) = isLogo ? (500, 180) : landscape ? (640, 360) : (264, 374);
        var id = System.Text.RegularExpressions.Regex.Match(url, @"/(?:co|ar|sc)?(\d+)[ab]?\.(?:jpg|png)").Groups[1].Value;
        var hue = (int.TryParse(id, out var n) ? n * 47 : 0) % 360;
        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(w, h));
        using (var ctx = rtb.CreateDrawingContext())
        {
            if (isLogo)
            {
                // A "clear logo": bold outlined lettering on a transparent background.
                var logoText = new Avalonia.Media.FormattedText("HAUNTED HOUSE",
                    System.Globalization.CultureInfo.InvariantCulture, Avalonia.Media.FlowDirection.LeftToRight,
                    new Avalonia.Media.Typeface("Inter", Avalonia.Media.FontStyle.Italic, Avalonia.Media.FontWeight.Black),
                    64, new Avalonia.Media.SolidColorBrush(Avalonia.Media.HsvColor.ToRgb(hue, 0.35, 1)));
                var geo = logoText.BuildGeometry(new Avalonia.Point((w - logoText.Width) / 2, (h - logoText.Height) / 2))!;
                ctx.DrawGeometry(new Avalonia.Media.SolidColorBrush(Avalonia.Media.HsvColor.ToRgb(hue, 0.35, 1)),
                    new Avalonia.Media.Pen(Avalonia.Media.Brushes.Black, 5), geo);
                goto done;
            }
            var top = Avalonia.Media.HsvColor.ToRgb(hue, 0.55, 0.85);
            var bottom = Avalonia.Media.HsvColor.ToRgb((hue + 40) % 360, 0.7, 0.35);
            var brush = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = { new(top, 0), new(bottom, 1) },
            };
            ctx.DrawRectangle(brush, null, new Avalonia.Rect(0, 0, w, h));
            ctx.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 255, 255, 255)), null,
                new Avalonia.Point(w * 0.7, h * 0.3), w * 0.35, w * 0.35);
            var text = new Avalonia.Media.FormattedText(landscape ? "KEY ART" : "COVER " + id,
                System.Globalization.CultureInfo.InvariantCulture, Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface("Inter", Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.Bold),
                landscape ? 48 : 26, Avalonia.Media.Brushes.White);
            ctx.DrawText(text, new Avalonia.Point(18, h - text.Height - 18));
        done:;
        }
        using var ms = new MemoryStream();
#pragma warning disable CS0618
        rtb.Save(ms);
#pragma warning restore CS0618
        return ms.ToArray();
    }

    [AvaloniaFact]
    public async Task Artwork_renders_in_grid_carousel_and_dialogs()
    {
        SeedGames();
        _igdb.ImageBytes = FakeArt;
        using var lib = _env.OpenLibrary();
        foreach (var t in new[] { "Kirby's Return to Dream Land Deluxe", "Haunted House", "Puzzle Quest", "Unmatched Homebrew" })
            lib.AddEdition(t, Generations.Switch1, _env.Game(t + ".nsp"));
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = Window(lib, ui, launcher);
        await vm.MatchUnmatchedCommand.ExecuteAsync(null);
        vm.Select(vm.Items.Single(i => i.Title == "Haunted House").Id);

        var window = new SwitchYard.App.Views.MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        MainWindowTests.Capture(window, "m4-grid.png");
        vm.IsCarousel = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250);
        window.CenterCarousel(animate: false);
        MainWindowTests.Capture(window, "m4-carousel.png");
        Assert.NotNull(vm.SelectedKeyArt);
        window.Close();

        var match = new MatchViewModel(lib.GetEdition(vm.Items.Single(i => i.Title == "Puzzle Quest").Id)!,
            new MetadataService(lib, _igdb.Client(), ArtDir));
        var matchWindow = new SwitchYard.App.Views.MatchWindow(match);
        matchWindow.Show();
        await match.StartAsync();
        MainWindowTests.Capture(matchWindow, "m4-match.png");
        matchWindow.Close();

        var settings = new SwitchYard.App.Views.IgdbSettingsWindow(new IgdbSettingsViewModel("", "", ui, (_, _) => _igdb.Client()));
        settings.Show();
        MainWindowTests.Capture(settings, "m4-igdb-settings.png");
        settings.Close();
    }

    /// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts to a context).</summary>
    private sealed class SyncProgress(Action<(int Done, int Total, string Title)> report) : IProgress<(int Done, int Total, string Title)>
    {
        public void Report((int Done, int Total, string Title) value) => report(value);
    }
}
