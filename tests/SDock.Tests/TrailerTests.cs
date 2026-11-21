using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>Trailers: found on IGDB, played inside S-Dock, driven by the controller.</summary>
public sealed class TrailerTests : IDisposable
{
    private readonly TestEnv _env = new();
    private readonly FakeIgdb _igdb = new();
    public TrailerTests() => MainViewModel.TrailerPlayerAvailable = () => true;
    public void Dispose()
    {
        MainViewModel.TrailerPlayerAvailable = () => SDock.App.Controls.TrailerView.RuntimeAvailable;
        _env.Dispose();
    }

    private static IgdbGame WithVideos(IgdbGame g, params (string Name, string Id)[] videos) =>
        g with { Videos = videos.Select(v => new IgdbVideo { Name = v.Name, VideoId = v.Id }).ToList() };

    private MainViewModel Vm(LibraryStore lib, FakeUi ui, bool keys = true)
    {
        if (keys) lib.IgdbCredentials = ("test-client-id", "test-secret");
        return new MainViewModel(lib, new EmulatorLauncher(), ui, l => l.HasIgdbCredentials ? _igdb.Client() : null);
    }

    [Fact]
    public void The_main_trailer_is_preferred()
    {
        var g = WithVideos(FakeIgdb.Game(1, "X"), ("Dev Diary", "d"), ("Announcement Trailer", "a"), ("Trailer", "t"));
        Assert.Equal("t", g.TrailerVideoId);
        Assert.Equal("a", WithVideos(FakeIgdb.Game(1, "X"), ("Dev Diary", "d"), ("Announcement Trailer", "a")).TrailerVideoId);
        Assert.Equal("d", WithVideos(FakeIgdb.Game(1, "X"), ("Dev Diary", "d")).TrailerVideoId);
        Assert.Null(FakeIgdb.Game(1, "X").TrailerVideoId);
    }

    [AvaloniaFact]
    public async Task Found_on_igdb_by_name_and_played_inside_s_dock()
    {
        _igdb.Games.Add(WithVideos(FakeIgdb.Game(2001, "Alpha Quest", FakeIgdb.Switch1), ("Trailer", "abc123")));
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha Quest", Generations.Switch1, _env.Game("A.nsp"));
        var vm = Vm(lib, new FakeUi());

        await vm.WatchTrailerCommand.ExecuteAsync(null);
        Assert.True(vm.IsTrailerOpen);
        Assert.Equal("abc123", vm.TrailerVideoId);
        Assert.Equal("Alpha Quest", vm.TrailerTitle);
        Assert.Equal("abc123", lib.GetTrailer(a)!.VideoId);          // kept for next time
        Assert.Null(lib.GetEdition(a)!.IgdbId);                       // the game's match wasn't changed

        var scripts = new List<string>();
        vm.TrailerScript += scripts.Add;
        vm.TrailerInput(PadAction.Accept);                           // A: pause
        vm.TrailerInput(PadAction.Right);                            // skip 10 s
        vm.TrailerInput(PadAction.Down);                             // quieter
        Assert.Equal(["toggle()", "seek(10)", "volume(-10)"], scripts);
        vm.TrailerInput(PadAction.Cancel);                           // B: close
        Assert.False(vm.IsTrailerOpen);
        Assert.Null(vm.TrailerVideoId);
    }

    [AvaloniaFact]
    public async Task A_matched_game_gets_its_trailer_with_the_match()
    {
        _igdb.Games.Add(WithVideos(FakeIgdb.Game(2002, "Bravo", FakeIgdb.Switch1), ("Launch Trailer", "lt1")));
        using var lib = _env.OpenLibrary();
        var b = lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        lib.IgdbCredentials = ("test-client-id", "test-secret");
        var service = new MetadataService(lib, _igdb.Client(), AppPaths.ArtworkDir);
        Assert.Equal(MatchOutcome.Matched, await service.AutoMatchAsync(lib.GetEdition(b)!));
        Assert.Equal("lt1", lib.GetTrailer(b)!.VideoId);
    }

    [AvaloniaFact]
    public async Task None_found_says_so_and_isnt_asked_again_straight_away()
    {
        _igdb.Games.Add(FakeIgdb.Game(2003, "Charlie", FakeIgdb.Switch1));   // no videos
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Charlie", Generations.Switch1, _env.Game("C.nsp"));
        var ui = new FakeUi();
        var vm = Vm(lib, ui);
        await vm.WatchTrailerCommand.ExecuteAsync(null);
        Assert.False(vm.IsTrailerOpen);
        Assert.Contains("IGDB has no trailer", ui.Messages.Last());
        var queries = _igdb.Queries.Count;
        await vm.WatchTrailerCommand.ExecuteAsync(null);
        Assert.Equal(queries, _igdb.Queries.Count);                   // remembered: IGDB isn't asked again
    }

    [AvaloniaFact]
    public async Task Without_igdb_keys_it_offers_to_add_them()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Delta", Generations.Switch1, _env.Game("D.nsp"));
        var ui = new FakeUi { ConfirmResult = false };
        var vm = Vm(lib, ui, keys: false);
        await vm.WatchTrailerCommand.ExecuteAsync(null);
        Assert.Contains("free IGDB", ui.Messages.Last());
        Assert.False(vm.IsTrailerOpen);
    }

    [AvaloniaFact]
    public async Task Without_webview2_it_explains()
    {
        MainViewModel.TrailerPlayerAvailable = () => false;
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Echo", Generations.Switch1, _env.Game("E.nsp"));
        var ui = new FakeUi();
        var vm = Vm(lib, ui);
        await vm.WatchTrailerCommand.ExecuteAsync(null);
        Assert.Contains("WebView2", ui.Messages.Last());
    }

    [AvaloniaFact]
    public void The_controller_drives_the_player_and_game_actions_offer_it()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Foxtrot", Generations.Switch1, _env.Game("F.nsp"));
        var vm = Vm(lib, new FakeUi());
        vm.IsCarousel = true;
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        GamepadService.Dispatch(window, PadAction.GameActions);      // X: the carousel's short menu
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.MenuItems, m => m.Label == "Watch Trailer");
        vm.CloseMenu();

        vm.OpenTrailer("Foxtrot", "vid");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Grid>("TrailerOverlay")!.IsVisible);
        var scripts = new List<string>();
        vm.TrailerScript += scripts.Add;
        GamepadService.Dispatch(window, PadAction.Accept);           // A goes to the player, not the covers
        GamepadService.Dispatch(window, PadAction.Cancel);           // B closes it
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["toggle()"], scripts);
        Assert.False(vm.IsTrailerOpen);
        Assert.False(vm.IsRunning);                                  // nothing was launched underneath
        window.Close();
    }
}
