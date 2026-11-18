using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>A stand-in for emuready.com's API: games by name, and PC and handheld reports per game.</summary>
public sealed class FakeEmuReady : HttpMessageHandler
{
    public List<(string Id, string Title, string System)> Games { get; } = [];
    public Dictionary<string, List<(string Emulator, string Rating, int Rank, string Os)>> Pc { get; } = [];
    public Dictionary<string, List<(string Emulator, string Rating, int Rank)>> Handheld { get; } = [];
    public List<string> Requests { get; } = [];
    public bool Offline { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Offline) throw new HttpRequestException("offline");
        var uri = request.RequestUri!;
        var procedure = uri.AbsolutePath.Split('/')[^1];
        Requests.Add(procedure);
        var input = JsonDocument.Parse(Uri.UnescapeDataString(uri.Query.Replace("?input=", ""))).RootElement.GetProperty("json");
        object data = procedure switch
        {
            "games.get" => new
            {
                games = Games.Where(g => g.Title.Contains(input.GetProperty("search").GetString()!, StringComparison.OrdinalIgnoreCase))
                    .Select(g => new { id = g.Id, title = g.Title, system = new { key = g.System }, _count = new { listings = 3 } }),
            },
            "pcListings.get" => Listings(input.GetProperty("gameId").GetString()!,
                Pc.GetValueOrDefault(input.GetProperty("gameId").GetString()!)?.Select(r => (r.Emulator, r.Rating, r.Rank, r.Os)) ?? []),
            "listings.byGame" => Listings(input.GetProperty("gameId").GetString()!,
                Handheld.GetValueOrDefault(input.GetProperty("gameId").GetString()!)?.Select(r => (r.Emulator, r.Rating, r.Rank, "")) ?? []),
            _ => new { },
        };
        var json = JsonSerializer.Serialize(new { result = new { data = new { json = data } } });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private static object Listings(string gameId, IEnumerable<(string Emulator, string Rating, int Rank, string Os)> reports) => new
    {
        listings = reports.Select(r => new { gameId, emulator = new { name = r.Emulator }, performance = new { label = r.Rating, rank = r.Rank }, os = r.Os }),
        pagination = new { hasNextPage = false },
    };
}

public sealed class EmuReadyTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Theory]
    [InlineData("Super Mario Bros.™ Wonder", "super mario bros wonder")]
    [InlineData("Yoshi’s Crafted World™", "yoshis crafted world")]
    [InlineData("Mortal Kombat: Legacy Kollection", "mortal kombat legacy kollection")]
    [InlineData("Pokémon Legends: Arceus", "pokemon legends arceus")]
    public void Titles_compare_without_marks_and_punctuation(string title, string normalized) =>
        Assert.Equal(normalized, EmuReadyClient.Normalize(title));

    [Fact]
    public void Matches_the_right_console_and_skips_unsure_matches()
    {
        List<EmuReadyGame> games =
        [
            new("pc", "Hades", "microsoft_windows", 9),
            new("ns", "Hades", EmuReadyClient.Switch1Key, 4),
            new("ns2", "Hades II", EmuReadyClient.Switch1Key, 2),
        ];
        Assert.Equal("ns", EmuReadyClient.BestMatch("Hades", Generations.Switch1, games)?.Id);
        Assert.Equal("ns2", EmuReadyClient.BestMatch("HADES II", Generations.Switch1, games)?.Id);
        Assert.Null(EmuReadyClient.BestMatch("Celeste", Generations.Switch1, games));
        // Switch 2 games fall back to the Switch 1 listing.
        Assert.Equal("ns", EmuReadyClient.BestMatch("Hades", Generations.Switch2, games)?.Id);
        Assert.Equal("Yoshi's Crafted World", EmuReadyClient.SearchText("Yoshi’s Crafted World™"));
        Assert.True(EmuReadyClient.SameEmulator("Ryujinx/Ryubing", "ryujinx"));
        Assert.False(EmuReadyClient.SameEmulator("Citron", "eden"));
    }

    [Fact]
    public void Each_emulator_gets_its_typical_rating_preferring_windows_reports()
    {
        var summary = EmuReadyClient.Summarize(
        [
            new("Eden", "Perfect", 1, "WINDOWS"), new("Eden", "Great", 2, "WINDOWS"), new("Eden", "Poor", 4, "WINDOWS"),
            new("Eden", "Nothing", 8, "LINUX"), // Linux is ignored when there are Windows reports
            new("Citron", "Playable", 3, "WINDOWS"),
        ]);
        Assert.Equal(["Eden", "Citron"], summary.Select(r => r.Emulator));
        Assert.Equal(("Great", 3), (summary[0].Rating, summary[0].Reports));
        var linuxOnly = EmuReadyClient.Summarize([new("Eden", "Great", 2, "LINUX")]);
        Assert.Equal("Great", Assert.Single(linuxOnly).Rating);
    }

    [Fact]
    public async Task Looks_up_a_game_with_handhelds_as_the_fallback()
    {
        var fake = new FakeEmuReady();
        fake.Games.Add(("g1", "Kirby and the Forgotten Land", EmuReadyClient.Switch1Key));
        fake.Games.Add(("g2", "Capcom Arcade Stadium", EmuReadyClient.Switch1Key));
        fake.Pc["g1"] = [("Eden", "Great", 2, "WINDOWS"), ("Eden", "Great", 2, "WINDOWS")];
        fake.Handheld["g2"] = [("Eden", "Playable", 3)];
        using var client = new EmuReadyClient(fake);

        var kirby = await client.LookUpAsync("Kirby™ and the Forgotten Land", Generations.Switch1);
        Assert.Equal(("g1", EmuReadySummary.Pc, 2), (kirby.GameId, kirby.Source, kirby.Reports));
        Assert.Equal("https://www.emuready.com/games/g1", kirby.Url);

        var capcom = await client.LookUpAsync("Capcom Arcade Stadium", Generations.Switch1);
        Assert.Equal(EmuReadySummary.Handheld, capcom.Source);
        Assert.Equal("Playable", capcom.For("eden")!.Rating);

        var missing = await client.LookUpAsync("Primal Light", Generations.Switch1);
        Assert.False(missing.Found);
        Assert.Equal(["games.get", "pcListings.get", "games.get", "pcListings.get", "listings.byGame", "games.get"], fake.Requests);
    }

    [AvaloniaFact]
    public async Task On_by_default_shown_under_play_and_kept()
    {
        var fake = new FakeEmuReady();
        fake.Games.Add(("g1", "Alpha Quest", EmuReadyClient.Switch1Key));
        fake.Pc["g1"] = [("Eden", "Perfect", 1, "WINDOWS")];
        using var lib = _env.OpenLibrary();
        var profile = _env.FakeProfile() with { Emulator = "eden" };
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);
        lib.AddEdition("Alpha Quest", Generations.Switch1, _env.Game("A.nsp"));
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui) { EmuReady = new EmuReadyClient(fake) };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ShowEmuReady);                              // on unless switched off
        await vm.RefreshEmuReadyAsync(vm.SelectedItem!);           // (what resting on the game does)
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("EmuReady: Perfect on Eden · 1 PC report", vm.EmuReadyText);
        Assert.Contains("EmuReady: Perfect", vm.SelectedTags);    // the carousel's tag
        window.UpdateLayout();
        Assert.True(window.FindControl<Button>("EmuReadyButton")!.IsEffectivelyVisible);
        vm.OpenEmuReadyCommand.Execute(null);
        Assert.Equal("https://www.emuready.com/games/g1", ui.OpenedUrls.Last());

        // Kept in the library: a restart shows it without asking again.
        var count = fake.Requests.Count;
        var again = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { EmuReady = new EmuReadyClient(fake) };
        Assert.Equal("EmuReady: Perfect on Eden · 1 PC report", again.EmuReadyText);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, fake.Requests.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_shows_nothing_and_tries_again_later()
    {
        var fake = new FakeEmuReady { Offline = true };
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha Quest", Generations.Switch1, _env.Game("A.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { EmuReady = new EmuReadyClient(fake) };
        vm.ShowEmuReady = true;
        await vm.RefreshEmuReadyAsync(vm.SelectedItem!);
        Assert.Equal("", vm.EmuReadyText);
        Assert.Null(vm.EmuReadyFor(vm.SelectedItem!));             // nothing kept, so it's asked again next time
    }

    [AvaloniaFact]
    public void Switched_off_shows_and_asks_nothing()
    {
        var fake = new FakeEmuReady();
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha Quest", Generations.Switch1, _env.Game("A.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { EmuReady = new EmuReadyClient(fake) };
        vm.ShowEmuReady = false;                                   // Settings → Game Info
        Assert.Equal("", vm.EmuReadyText);
        Assert.False(new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()).ShowEmuReady); // remembered
        Assert.Empty(fake.Requests);
    }
}
