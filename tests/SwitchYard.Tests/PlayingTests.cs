using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SwitchYard.App.Input;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

namespace SwitchYard.Tests;

/// <summary>Full screen, leaving a game with Esc / Start+Select, and hiding games.</summary>
public sealed class PlayingTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    // ------------------------------------------------------------ full screen launch
    [Fact]
    public void Full_screen_switch_is_added_to_eden_profiles_once()
    {
        var eden = EmulatorProfile.New("Eden", "eden.exe", LaunchArgs.EdenPreset, emulator: "eden");
        Assert.Equal(["-f", "-g", LaunchArgs.GamePlaceholder], LaunchArgs.WithFullScreen(eden).Args);
        var already = eden with { Args = ["--fullscreen", "-g", LaunchArgs.GamePlaceholder] };
        Assert.Same(already, LaunchArgs.WithFullScreen(already));
        var custom = EmulatorProfile.New("Other", "other.exe", ["{game}"]);
        Assert.Same(custom, LaunchArgs.WithFullScreen(custom)); // other emulators: their own switch goes in the profile
    }

    [AvaloniaFact]
    public void Play_starts_eden_in_full_screen_unless_turned_off()
    {
        using var lib = _env.OpenLibrary();
        var profile = _env.FakeProfile() with { Emulator = "eden" };
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        Assert.True(vm.StartGamesFullScreen);

        vm.PlayCommand.Execute(null);
        Ui.WaitUntil(() => !vm.IsRunning && File.Exists(_env.RecordPath), 15000);
        Assert.Equal("-f", _env.ReadRecord().GetProperty("args")[0].GetString());

        File.Delete(_env.RecordPath);
        vm.StartGamesFullScreen = false;
        vm.PlayCommand.Execute(null);
        Ui.WaitUntil(() => !vm.IsRunning && File.Exists(_env.RecordPath), 15000);
        Assert.DoesNotContain(_env.ReadRecord().GetProperty("args").EnumerateArray(), a => a.GetString() == "-f");

        Assert.False(new MainViewModel(lib, launcher, new FakeUi()).StartGamesFullScreen); // remembered
    }

    // ------------------------------------------------------------ leaving a game
    [Fact]
    public void Exit_gate_one_press_or_two_within_the_window()
    {
        var gate = new ExitPressGate();
        Assert.True(gate.Press(TimeSpan.Zero, twoPresses: false));

        Assert.False(gate.Press(TimeSpan.FromSeconds(10), twoPresses: true)); // first press only arms it
        Assert.True(gate.Press(TimeSpan.FromSeconds(11), twoPresses: true));

        Assert.False(gate.Press(TimeSpan.FromSeconds(20), twoPresses: true));
        Assert.False(gate.Press(TimeSpan.FromSeconds(20) + ExitPressGate.Window + TimeSpan.FromSeconds(1), twoPresses: true)); // too late: arms again
        gate.Reset();
        Assert.False(gate.IsArmed(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Close_ends_a_running_emulator()
    {
        using var launcher = new EmulatorLauncher();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        launcher.Finished += (_, _, _) => finished.TrySetResult();
        Assert.True(launcher.Launch(_env.FakeProfile(sleep: 60), _env.Game(), "e"));
        Assert.NotNull(launcher.ProcessId);
        launcher.Close(TimeSpan.FromSeconds(1)); // the stand-in has no window to close, so it is stopped
        await finished.Task.WaitAsync(Timeout);
        Assert.False(launcher.IsRunning);
    }

    [AvaloniaFact]
    public void Exit_setting_defaults_to_two_presses_and_is_remembered()
    {
        using var lib = _env.OpenLibrary();
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        Assert.True(vm.ExitWithTwoPresses);
        Assert.False(vm.ExitWithOnePress);
        vm.ExitWithOnePress = true;
        Assert.False(vm.ExitWithTwoPresses);
        Assert.True(new MainViewModel(lib, launcher, new FakeUi()).ExitWithOnePress);
    }

    // ------------------------------------------------------------ full-screen carousel
    [AvaloniaFact]
    public void Carousel_is_full_screen_and_grid_restores_the_window()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        Assert.Equal(WindowState.Normal, window.WindowState);

        vm.IsCarousel = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.FullScreen, window.WindowState);

        vm.IsCarousel = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.Close();
    }

    // ------------------------------------------------------------ hiding games
    [Fact]
    public void Hidden_games_persist()
    {
        string id;
        using (var lib = _env.OpenLibrary())
        {
            id = lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
            lib.SetHidden(id, true);
            lib.SetHidden(id, true); // twice is harmless
        }
        using (var lib = _env.OpenLibrary())
        {
            Assert.Equal([id], lib.HiddenEditions());
            lib.SetHidden(id, false);
            Assert.Empty(lib.HiddenEditions());
        }
    }

    [AvaloniaFact]
    public void Hide_game_moves_it_to_hidden_games_and_unhide_brings_it_back()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("Alpha.nsp"));
        var b = lib.AddEdition("Bravo", Generations.Switch1, _env.Game("Bravo.nsp"));
        lib.AddEdition("Charlie", Generations.Switch1, _env.Game("Charlie.nsp"));
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        vm.Select(b);
        Assert.Equal("Hide Game", vm.HideMenuLabel);

        vm.ToggleHiddenCommand.Execute(null);
        Assert.Equal(["Alpha", "Charlie"], vm.Items.Select(i => i.Title));
        Assert.Equal("Charlie", vm.SelectedItem?.Title); // the neighbour, not back to the start
        Assert.Equal(2, vm.LibraryTabs.First(t => t.Id == Libraries.Main).Count);
        Assert.Contains("1 hidden", $"{vm.StatusText} {new MainViewModel(lib, launcher, new FakeUi()).StatusText}");

        vm.MoreFilter = vm.MoreFilters.First(f => f.Value == FilterOption.HiddenValue);
        Assert.Equal(["Bravo"], vm.Items.Select(i => i.Title));
        Assert.Equal("Unhide Game", vm.HideMenuLabel);
        vm.ToggleHiddenCommand.Execute(null);
        Assert.Empty(vm.Items);
        Assert.Contains("No hidden games", vm.EmptyText);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], vm.Items.Select(i => i.Title));
        Assert.NotNull(a);
    }
}
