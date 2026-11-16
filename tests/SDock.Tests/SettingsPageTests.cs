using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>The Settings page: the same in both views, and driven by controller, keyboard or mouse.</summary>
public sealed class SettingsPageTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (MainViewModel Vm, MainWindow Window, FakeUi Ui) Open(LibraryStore lib, bool carousel = false)
    {
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui) { IsCarousel = carousel };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, window, ui);
    }

    private static void Pad(MainWindow window, params PadAction[] actions)
    {
        foreach (var a in actions)
        {
            GamepadService.Dispatch(window, a);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void GoToSection(MainWindow window, MainViewModel vm, string title)
    {
        var target = vm.SettingsSections.ToList().FindIndex(s => s.Title == title);
        while (vm.SettingsInRows) Pad(window, PadAction.Cancel);
        while (vm.SettingsSectionIndex < target) Pad(window, PadAction.Down);
        while (vm.SettingsSectionIndex > target) Pad(window, PadAction.Up);
        Pad(window, PadAction.Accept);
        Assert.True(vm.SettingsInRows);
    }

    private static void GoToRow(MainWindow window, MainViewModel vm, string label)
    {
        var target = vm.SettingsRows.ToList().FindIndex(r => r.Label == label);
        Assert.True(target >= 0, $"No setting {label}: {string.Join(", ", vm.SettingsRows.Select(r => r.Label))}");
        while (vm.SettingsRowIndex < target) Pad(window, PadAction.Down);
        while (vm.SettingsRowIndex > target) Pad(window, PadAction.Up);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opens_the_same_in_both_views_and_backs_out_one_step_at_a_time(bool carousel)
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game());
        var (vm, window, _) = Open(lib, carousel);

        Pad(window, PadAction.Menu);                               // Select
        Assert.True(vm.IsSettingsOpen);
        Assert.True(window.FindControl<Border>("SettingsPage")!.IsEffectivelyVisible);
        Assert.Equal(["Library", "Emulators", "Playing", "Appearance", "Game Info", "About"], vm.SettingsSections.Select(s => s.Title));
        Assert.False(vm.SettingsInRows);
        Assert.Equal("Close", vm.SettingsBackLabel);

        Pad(window, PadAction.Down, PadAction.Down);               // to Playing
        Assert.Equal("Playing", vm.SettingsSection?.Title);
        Pad(window, PadAction.Right);                              // into its settings
        Assert.True(vm.SettingsInRows);
        Assert.Equal("Back", vm.SettingsBackLabel);
        Pad(window, PadAction.Cancel);                             // B: back to the sections, still open
        Assert.True(vm.IsSettingsOpen);
        Assert.False(vm.SettingsInRows);
        Assert.Equal("Playing", vm.SettingsSection?.Title);
        Pad(window, PadAction.Cancel);                             // B again: closed
        Assert.False(vm.IsSettingsOpen);
        Assert.Equal(carousel, vm.IsCarousel);                     // back where you were
        window.Close();
    }

    [AvaloniaFact]
    public void Switches_and_choices_with_the_controller()
    {
        using var lib = _env.OpenLibrary();
        var (vm, window, _) = Open(lib);
        Pad(window, PadAction.Menu);

        GoToSection(window, vm, "Playing");
        GoToRow(window, vm, "Start games in full screen");
        Assert.True(vm.SettingsRow!.On);
        Pad(window, PadAction.Accept);                             // A flips it
        Assert.False(vm.StartGamesFullScreen);
        Assert.False(vm.SettingsRow!.On);

        GoToRow(window, vm, "Leave a game with Esc or Start + Select");
        Assert.Equal("Press twice", vm.SettingsRow!.ValueText);
        Pad(window, PadAction.Right);                              // Right: next value
        Assert.Equal("Press once", vm.SettingsRow!.ValueText);
        Assert.True(vm.ExitWithOnePress);
        Pad(window, PadAction.Left);                               // Left: back again
        Assert.True(vm.ExitWithTwoPresses);

        GoToSection(window, vm, "Appearance");
        GoToRow(window, vm, "Background");
        Pad(window, PadAction.Accept);                             // A on a choice moves it on too
        Assert.Equal(MainViewModel.BackgroundColor, vm.BackgroundStyle);
        Assert.True(vm.IsSettingsOpen);                            // settings never close by themselves
        window.Close();
    }

    [AvaloniaFact]
    public void Default_emulators_are_chosen_right_on_the_page()
    {
        using var lib = _env.OpenLibrary();
        var profile = _env.FakeProfile() with { Generation = Generations.Both };
        lib.SaveProfile(profile);
        var (vm, window, _) = Open(lib);
        Pad(window, PadAction.Menu);
        GoToSection(window, vm, "Emulators");

        GoToRow(window, vm, "Switch 2 default");
        Assert.Equal("Ask each time", vm.SettingsRow!.ValueText);
        Pad(window, PadAction.Right);
        Assert.Equal("No emulator (run the game's .exe)", vm.SettingsRow!.ValueText);
        Assert.Equal(LaunchRouter.NoEmulatorId, lib.GetGenerationDefault(Generations.Switch2));
        Pad(window, PadAction.Right);
        Assert.Equal(profile.Name, vm.SettingsRow!.ValueText);
        Assert.Equal(profile.Id, lib.GetGenerationDefault(Generations.Switch2));

        GoToRow(window, vm, "Switch 1 default");
        Assert.DoesNotContain("No emulator", vm.SettingsRow!.ValueText);
        Assert.Equal("1 profile", vm.SettingsRows.Single(r => r.Label == "Emulator Profiles…").ValueText);
        window.Close();
    }

    [AvaloniaFact]
    public void A_dialog_opened_from_settings_comes_back_to_the_same_setting()
    {
        using var lib = _env.OpenLibrary();
        var (vm, window, ui) = Open(lib);
        Pad(window, PadAction.Menu);
        GoToSection(window, vm, "Library");
        GoToRow(window, vm, "Add Game (.exe)…");
        var row = vm.SettingsRowIndex;

        Pad(window, PadAction.Accept);                             // opens the file picker; cancelled (FakeUi)
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSettingsOpen);
        Assert.True(vm.SettingsInRows);
        Assert.Equal(row, vm.SettingsRowIndex);

        GoToSection(window, vm, "Game Info");
        GoToRow(window, vm, "IGDB and SteamGridDB keys…");
        Pad(window, PadAction.Accept);                             // the keys dialog (FakeUi: closed)
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSettingsOpen);
        Assert.Equal("IGDB and SteamGridDB keys…", vm.SettingsRow?.Label);
        window.Close();
    }

    [AvaloniaFact]
    public void Keyboard_drives_it_too()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game());
        var (vm, window, _) = Open(lib);
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();

        void Key(Key key)
        {
            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Interactive ?? window;
            focused.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = focused });
            Dispatcher.UIThread.RunJobs();
        }

        Key(Avalonia.Input.Key.Down);
        Assert.Equal("Emulators", vm.SettingsSection?.Title);
        Key(Avalonia.Input.Key.Enter);
        Assert.True(vm.SettingsInRows);
        Key(Avalonia.Input.Key.Escape);
        Assert.False(vm.SettingsInRows);
        Assert.True(vm.IsSettingsOpen);
        Key(Avalonia.Input.Key.Escape);
        Assert.False(vm.IsSettingsOpen);
        Assert.Equal("Alpha", vm.SelectedItem?.Title);             // the arrows never moved the games underneath
        window.Close();
    }

    [AvaloniaFact]
    public void Mouse_clicks_sections_settings_and_arrows()
    {
        using var lib = _env.OpenLibrary();
        var (vm, window, _) = Open(lib);
        window.FindControl<Button>("SettingsButton")!.Command!.Execute(null); // the gear
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSettingsOpen);

        vm.SettingsSectionIndex = vm.SettingsSections.ToList().FindIndex(s => s.Title == "Appearance"); // click a section
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var list = window.FindControl<ListBox>("SettingsRowList")!;
        var next = list.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "›");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));    // the › arrow
        Assert.Equal(MainViewModel.BackgroundColor, vm.BackgroundStyle);
        var previous = list.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "‹");
        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(MainViewModel.BackgroundArt, vm.BackgroundStyle);

        window.FindControl<Button>("SettingsCloseButton")!.Command!.Execute(null);
        Assert.False(vm.IsSettingsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Launch_view_is_what_S_Dock_opens_in_and_Y_does_not_change_it()
    {
        using var lib = _env.OpenLibrary();
        var (vm, window, _) = Open(lib);
        Pad(window, PadAction.Menu);
        GoToSection(window, vm, "Library");
        GoToRow(window, vm, "Launch view");
        Assert.Equal("Grid", vm.SettingsRow!.ValueText);
        Pad(window, PadAction.Right);
        Assert.Equal("Carousel", vm.SettingsRow!.ValueText);
        Assert.False(vm.IsCarousel);                               // only the next launch changes
        window.Close();

        Assert.True(new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()).IsCarousel);

        var again = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        again.ToggleViewCommand.Execute(null);                     // Y: back to the grid for now
        Assert.False(again.IsCarousel);
        Assert.True(new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()).IsCarousel);
    }

    [AvaloniaFact]
    public void Matching_on_IGDB_starts_only_from_Start_now()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game());
        var (vm, window, _) = Open(lib);
        Pad(window, PadAction.Menu);
        GoToSection(window, vm, "Game Info");
        var heading = vm.SettingsRows.Single(r => r.Label == "Match unmatched games on IGDB");
        Assert.False(heading.Selectable);                          // the heading can't be chosen
        Assert.Equal("1 game unmatched", heading.ValueText);
        var index = vm.SettingsRows.IndexOf(heading);
        Assert.Equal("Start now", vm.SettingsRows[index + 1].Label);
        Assert.True(vm.SettingsRows[index + 1].IsAction);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Game_info_shows_progress_and_cancel_while_an_update_runs()
    {
        using var lib = _env.OpenLibrary();
        var (vm, window, _) = Open(lib);
        Pad(window, PadAction.Menu);
        GoToSection(window, vm, "Game Info");
        Assert.DoesNotContain(vm.SettingsRows, r => r.Label == "Cancel");
        Assert.Equal("Not downloaded yet", vm.SettingsRows.Single(r => r.Label == "Update from TitleDB").ValueText);
        Assert.Equal("Not set", vm.SettingsRows.Single(r => r.Label == "IGDB and SteamGridDB keys…").ValueText);
        await Task.CompletedTask;
        window.Close();
    }
}
