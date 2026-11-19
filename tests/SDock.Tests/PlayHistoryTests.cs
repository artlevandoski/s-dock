using Avalonia.Headless.XUnit;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Play history: last played, total play time, Recently Played, and opening on the last game played.</summary>
public sealed class PlayHistoryTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Theory]
    [InlineData(0.5, "under a minute")]
    [InlineData(25, "25 min")]
    [InlineData(60, "1 h")]
    [InlineData(80, "1 h 20 min")]
    [InlineData(725, "12 h")]
    public void Play_time_reads_naturally(double minutes, string text) =>
        Assert.Equal(text, PlayStats.DescribeTime(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Days_read_naturally()
    {
        var now = new DateTime(2026, 11, 19, 20, 0, 0); // a Thursday
        Assert.Equal("today", PlayStats.DescribeDay(now.AddHours(-3), now));
        Assert.Equal("yesterday", PlayStats.DescribeDay(now.AddDays(-1), now));
        Assert.Equal(new DateTime(2026, 11, 17).ToString("dddd"), PlayStats.DescribeDay(new DateTime(2026, 11, 17, 9, 0, 0), now));
        Assert.Equal(new DateTime(2026, 10, 2).ToString("d MMM"), PlayStats.DescribeDay(new DateTime(2026, 10, 2), now));
        Assert.Equal(new DateTime(2025, 10, 2).ToString("d MMM yyyy"), PlayStats.DescribeDay(new DateTime(2025, 10, 2), now));
    }

    [Fact]
    public void Sessions_add_up_and_short_or_runaway_ones_are_handled()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var t0 = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);
        Assert.False(lib.RecordPlay(a, TimeSpan.FromSeconds(10), t0));           // a game that didn't really start
        Assert.Empty(lib.AllPlayStats());
        Assert.True(lib.RecordPlay(a, TimeSpan.FromMinutes(50), t0));
        Assert.True(lib.RecordPlay(a, TimeSpan.FromHours(40), t0.AddDays(1)));   // the PC slept with the game open
        var stats = lib.AllPlayStats()[a];
        Assert.Equal(2, stats.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(50) + LibraryStore.MaximumSession, stats.Total);
        Assert.Equal(t0.AddDays(1), stats.LastPlayedUtc);
        lib.ClearPlayHistory();
        Assert.Empty(lib.AllPlayStats());
    }

    [AvaloniaFact]
    public void Recently_played_tab_most_recent_first_with_a_played_line_on_each_game()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var b = lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        lib.AddEdition("Charlie", Generations.Switch1, _env.Game("C.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        Assert.Equal(["Main Library", "Arcade Archives", "Favorites", "Recently Played"], vm.LibraryTabs.Select(t => t.Label));

        vm.RecordSession(b, TimeSpan.FromHours(2));                  // a game closing after two hours
        vm.RecordSession(a, TimeSpan.FromMinutes(25));               // then another, later
        vm.SelectedLibraryTab = vm.LibraryTabs.Single(t => t.Id == Libraries.Recent);
        Assert.Equal(["Alpha", "Bravo"], vm.Items.Select(i => i.Title)); // most recent first; Charlie was never played
        Assert.Equal(2, vm.LibraryTabs.Single(t => t.Id == Libraries.Recent).Count);
        Assert.Null(vm.JumpToLetter(+1));                            // date order: no letter jumps here

        vm.Select(b);
        Assert.Equal("Played 2 h · last played today", vm.PlayHistoryText);
        Assert.Contains("Played 2 h · today", vm.SelectedTags);      // the carousel's tag

        vm.RecordSession(b, TimeSpan.FromSeconds(5));                // too short: nothing changes
        Assert.Equal("Played 2 h · last played today", vm.PlayHistoryText);
    }

    [AvaloniaFact]
    public void Opens_on_the_last_game_played_unless_set_otherwise()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var aca = lib.AddEdition("Arcade Archives Galaga", Generations.Switch1, _env.Game("G.nsp"));
        lib.RecordPlay(aca, TimeSpan.FromMinutes(10), DateTime.UtcNow.AddHours(-1));
        lib.RecordPlay(a, TimeSpan.FromMinutes(10), DateTime.UtcNow.AddDays(-2));
        lib.SetSetting(MainViewModel.LastSelectedKey, a);

        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        Assert.Equal("Arcade Archives Galaga", vm.SelectedTitle);   // played last, in its own library
        Assert.True(vm.IsArcade);

        vm.StartOnLastPlayedGame = false;                            // Settings → Library → Start on
        vm.SelectedLibraryTab = vm.LibraryTabs[0];
        vm.Select(a);
        var again = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        Assert.Equal("Alpha", again.SelectedTitle);                  // the last one selected instead
    }

    [AvaloniaFact]
    public void Playing_a_game_records_the_session()
    {
        using var lib = _env.OpenLibrary();
        var profile = _env.FakeProfile(sleep: 1.5);
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        var minimum = LibraryStore.MinimumSession;
        LibraryStore.MinimumSession = TimeSpan.FromSeconds(1);      // the real minimum is 30 seconds
        try
        {
            vm.PlayCommand.Execute(null);
            Ui.WaitUntil(() => !vm.IsRunning && File.Exists(_env.RecordPath), 15000);
        }
        finally
        {
            LibraryStore.MinimumSession = minimum;
        }
        var stats = lib.AllPlayStats()[a];
        Assert.Equal(1, stats.Sessions);
        Assert.InRange(stats.TotalSeconds, 1, 10);
        Assert.StartsWith("Played under a minute · last played today", vm.PlayHistoryText);
    }

    [AvaloniaFact]
    public async Task Clear_play_history_asks_first()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.RecordPlay(a, TimeSpan.FromMinutes(10), DateTime.UtcNow);
        var ui = new FakeUi { ConfirmResult = false };
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        Assert.Equal("1 game played", vm.PlayedCountText);
        await vm.ClearPlayHistoryCommand.ExecuteAsync(null);
        Assert.NotEmpty(lib.AllPlayStats());                         // said no
        ui.ConfirmResult = true;
        await vm.ClearPlayHistoryCommand.ExecuteAsync(null);
        Assert.Empty(lib.AllPlayStats());
        Assert.Equal("", vm.PlayHistoryText);
    }
}
