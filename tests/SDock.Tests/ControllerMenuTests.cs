using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>Everything reachable with a controller: the on-screen menus, right-click, letter jumps, carousel text.</summary>
public sealed class ControllerMenuTests : IDisposable
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
        window.FocusSelectedCover();
        return (vm, window, ui);
    }

    private static void Press(MainWindow window, params PadAction[] actions)
    {
        foreach (var a in actions)
        {
            GamepadService.Dispatch(window, a);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Moves down to the line with this label (as the D-pad would) and presses A.</summary>
    private static void Choose(MainWindow window, MainViewModel vm, string label)
    {
        var index = vm.MenuItems.ToList().FindIndex(m => m.Label == label);
        Assert.True(index >= 0, $"No menu line {label}: {string.Join(", ", vm.MenuItems.Select(m => m.Label))}");
        var list = window.FindControl<ListBox>("MenuList")!;
        while (list.SelectedIndex < index) Press(window, PadAction.Down);
        while (list.SelectedIndex > index) Press(window, PadAction.Up);
        Press(window, PadAction.Accept);
    }

    [AvaloniaFact]
    public void Settings_menu_works_with_the_controller()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var (vm, window, _) = Open(lib);

        Press(window, PadAction.Menu); // Select / Back
        Assert.True(vm.IsMenuOpen);
        Assert.Equal("Settings", vm.MenuTitle);

        Choose(window, vm, "Background");
        Assert.Equal("Background", vm.MenuTitle);
        Choose(window, vm, "Color from Game");
        Assert.Equal(MainViewModel.BackgroundColor, vm.BackgroundStyle);
        Assert.True(vm.IsMenuOpen); // settings apply and the menu stays open
        Assert.True(vm.MenuItems.Single(m => m.Label == "Color from Game").IsChecked);
        Assert.False(vm.MenuItems.Single(m => m.Label == "Game Artwork").IsChecked);

        Press(window, PadAction.Cancel); // B: back to Settings
        Assert.Equal("Settings", vm.MenuTitle);
        Choose(window, vm, "Arcade Archives");
        Choose(window, vm, "Include SEGA AGES");
        Assert.True(vm.IncludeSegaAges);
        Press(window, PadAction.Cancel);
        Choose(window, vm, "Playing");
        Choose(window, vm, "Start Games in Full Screen");
        Assert.False(vm.StartGamesFullScreen);
        Press(window, PadAction.Cancel, PadAction.Cancel);
        Assert.False(vm.IsMenuOpen);

        // Filters are reachable too.
        Press(window, PadAction.Menu);
        Choose(window, vm, "Filter");
        Choose(window, vm, "Missing files");
        Assert.Equal(FilterOption.MissingValue, vm.GenerationFilter.Value);
        window.Close();
    }

    [AvaloniaFact]
    public void Game_menu_from_x_and_right_click_runs_actions()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        var zed = lib.AddEdition("Zed", Generations.Switch1, _env.Game("Z.nsp"));
        var (vm, window, ui) = Open(lib, carousel: true);
        vm.Select(zed);

        // Game Actions are off in the carousel for now: X and right-click do nothing, and the hint is hidden.
        Press(window, PadAction.GameActions);
        Assert.False(vm.IsMenuOpen);
        var carouselCover = window.FindControl<ListBox>("CarouselList")!.GetVisualDescendants().OfType<ListBoxItem>().First();
        carouselCover.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = carouselCover });
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsMenuOpen);
        Assert.False(window.FindControl<TextBlock>("GameActionsHint")!.IsEffectivelyVisible);

        // In the grid they work.
        Press(window, PadAction.ToggleView);
        Assert.True(vm.IsGrid);
        Assert.True(window.FindControl<TextBlock>("GameActionsHint")!.IsEffectivelyVisible);
        Press(window, PadAction.GameActions); // X
        Assert.True(vm.IsMenuOpen);
        Assert.Equal("Zed", vm.MenuTitle);
        var labels = vm.MenuItems.Select(m => m.Label).ToList();
        Assert.Contains("Play", labels);
        Assert.Contains("Launch With…", labels);
        Assert.Contains("Edit Carousel Text…", labels);
        Press(window, PadAction.GameActions);                // X again closes it
        Assert.False(vm.IsMenuOpen);

        // Right-click a cover: the same menu, for that cover.
        var list = window.FindControl<ListBox>("CoverList")!;
        var bravo = list.GetVisualDescendants().OfType<ListBoxItem>().First(i => (i.DataContext as EditionItem)?.Title == "Bravo");
        bravo.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = bravo });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bravo", vm.MenuTitle);
        Assert.Equal("Bravo", vm.SelectedItem?.Title);

        // Choosing an action closes the menu and runs it.
        ui.OnEditText = (_, _) => "Aaa";
        Choose(window, vm, "Sort Title…");
        Assert.False(vm.IsMenuOpen);
        Assert.Equal("Aaa", lib.ListEditions().Single(e => e.Title == "Bravo").SortTitle);

        // Pages: Game Info and Matching, then back out with B.
        Press(window, PadAction.GameActions);
        Choose(window, vm, "Game Info and Matching");
        Assert.Contains(vm.MenuItems, m => m.Label == "Clear Match and Artwork…");
        Assert.False(vm.MenuItems.Single(m => m.Label == "Remove IGDB Match").Enabled); // not matched
        Assert.Equal("Back", vm.MenuBackLabel);
        Press(window, PadAction.Cancel, PadAction.Cancel);
        Assert.False(vm.IsMenuOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Letter_jumps_with_up_down_triggers_and_page_keys()
    {
        using var lib = _env.OpenLibrary();
        foreach (var t in new[] { "1942", "Alpha", "Apex", "Bravo", "Zed" })
            lib.AddEdition(t, Generations.Switch1, _env.Game(t + ".nsp"));
        var zelda = lib.AddEdition("The Legend of Zelda", Generations.Switch1, _env.Game("Zelda.nsp"));
        lib.SetSortTitle(zelda, "Legend of Zelda");
        var (vm, window, _) = Open(lib, carousel: true);
        vm.Select(lib.ListEditions().Single(e => e.Title == "1942").Id);
        window.FocusSelectedCover();

        Press(window, PadAction.Down);              // carousel: next letter
        Assert.Equal("Alpha", vm.SelectedItem?.Title);
        Assert.True(vm.ShowJumpLetter);
        Assert.Equal("A", vm.JumpLetter);
        Press(window, PadAction.Down);
        Assert.Equal("Bravo", vm.SelectedItem?.Title);
        Press(window, PadAction.NextLetter);        // RT
        Assert.Equal("The Legend of Zelda", vm.SelectedItem?.Title); // filed under L by its sort title
        Press(window, PadAction.Up);
        Assert.Equal("Bravo", vm.SelectedItem?.Title);
        Press(window, PadAction.PreviousLetter);    // LT: start of the previous letter
        Assert.Equal("Alpha", vm.SelectedItem?.Title);
        Press(window, PadAction.PreviousLetter);
        Assert.Equal("1942", vm.SelectedItem?.Title);
        Press(window, PadAction.PreviousLetter);    // nowhere further
        Assert.Equal("1942", vm.SelectedItem?.Title);

        vm.IsCarousel = false;                      // grid: Up/Down move rows, Page Down jumps letters
        Dispatcher.UIThread.RunJobs();
        window.FocusSelectedCover();
        GamepadService.SendKey(window, Key.PageDown);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Alpha", vm.SelectedItem?.Title);
        window.Close();
    }

    [Theory]
    [InlineData("Explore a vast world. Fight monsters and more.", "Explore a vast world.")]
    [InlineData("Is this it? Yes.", "Is this it?")]
    [InlineData("Vol. 2 of the series, now on the go! Play anywhere.", "Vol. 2 of the series, now on the go!")]
    [InlineData("Version 2.5 adds co-op... and more. Second.", "Version 2.5 adds co-op... and more.")]
    [InlineData("STEP IN\nThe adventure begins here. Then more.", "The adventure begins here.")]
    [InlineData("A line without a stop\nSecond line.", "A line without a stop")]
    [InlineData("“Quoted line.” Next.", "“Quoted line.”")]
    [InlineData("", "")]
    public void First_sentence(string text, string expected) => Assert.Equal(expected, Sentences.First(text));

    [Fact]
    public void Long_first_sentence_is_cut_at_a_word()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 80)) + ".";
        var first = Sentences.First(text, 60);
        Assert.True(first.Length <= 60);
        Assert.EndsWith("word…", first);
    }

    [AvaloniaFact]
    public async Task Carousel_shows_the_first_sentence_and_can_be_edited()
    {
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.SaveMetadata(EditionMetadata.Empty(id) with { TdbSummary = "First sentence here. Second sentence there." });
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        vm.Select(id);
        Assert.Equal("First sentence here.", vm.CarouselSummary);
        Assert.Equal("First sentence here. Second sentence there.", vm.SelectedSummary); // the details keep it all

        ui.OnEditText = (_, initial) => { Assert.Equal("First sentence here.", initial); return "My own line."; };
        await vm.EditCarouselTextCommand.ExecuteAsync(null);
        Assert.Equal("My own line.", vm.CarouselSummary);
        Assert.Equal("My own line.", lib.GetMetadata(id).UserShortSummary);

        ui.OnEditText = (_, _) => "";
        await vm.EditCarouselTextCommand.ExecuteAsync(null);
        Assert.Null(lib.GetMetadata(id).UserShortSummary);
        Assert.Equal("First sentence here.", vm.CarouselSummary);
    }

    [Fact]
    public void Triggers_and_select_map_to_actions()
    {
        var mapper = new GamepadMapper();
        mapper.Reset(new PadState(PadButtons.None));
        Assert.Equal([PadAction.NextLetter], mapper.Update(new PadState(PadButtons.None, RightTrigger: 200), TimeSpan.Zero));
        Assert.Empty(mapper.Update(new PadState(PadButtons.None, RightTrigger: 255), TimeSpan.FromSeconds(1))); // held: once
        Assert.Empty(mapper.Update(new PadState(PadButtons.None, LeftTrigger: 60), TimeSpan.FromSeconds(2)));  // light pull
        Assert.Equal([PadAction.PreviousLetter], mapper.Update(new PadState(PadButtons.None, LeftTrigger: 140), TimeSpan.FromSeconds(3)));
        Assert.Equal([PadAction.Menu], mapper.Update(new PadState(PadButtons.Back, LeftTrigger: 140), TimeSpan.FromSeconds(4)));
    }

    [AvaloniaFact]
    public void Carousel_is_clean_and_the_grid_keeps_its_tools()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var (vm, window, _) = Open(lib, carousel: true);
        bool Shown(Control c) => c.IsEffectivelyVisible;
        var search = window.FindControl<TextBox>("SearchBox")!;
        var filters = window.FindControl<Grid>("FilterBar")!;
        var addFolder = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == vm.AddFolderCommand);
        Assert.False(Shown(search));
        Assert.False(Shown(filters));          // filter pills, More filters, Clear and the Grid/Carousel buttons
        Assert.False(Shown(addFolder));
        Assert.True(Shown(window.FindControl<Button>("SettingsButton")!));
        Assert.Null(window.FindControl<Button>("GameActionsButton")); // X opens Game Actions in the carousel
        var play = window.FindControl<Button>("CarouselPlayButton")!;
        var route = window.FindControl<TextBlock>("CarouselRouteText")!;
        Assert.True(route.Bounds.Top >= play.Bounds.Bottom); // the emulator line sits under Play

        GamepadService.Dispatch(window, PadAction.ToggleView); // Y: the grid brings them back
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsGrid);
        Assert.True(Shown(search));
        Assert.True(Shown(filters));
        Assert.True(Shown(addFolder));
        window.Close();
    }

    [AvaloniaFact]
    public void Carousel_shows_the_whole_title()
    {
        using var lib = _env.OpenLibrary();
        var longName = "Tales of the Shire A The Lord of The Rings Game Deluxe Edition with Every Expansion Pack";
        var id = lib.AddEdition(longName, Generations.Switch1, _env.Game("Long.nsp"));
        lib.AddEdition("Short", Generations.Switch1, _env.Game("Short.nsp"));
        var (vm, window, _) = Open(lib, carousel: true);
        var fit = window.FindControl<Viewbox>("CarouselTitleFit")!;
        var title = window.FindControl<TextBlock>("CarouselTitle")!;
        var band = window.FindControl<Border>("CarouselInfoBand")!;

        vm.Select(id);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(longName, title.Text);
        Assert.Equal(TextTrimming.None, title.TextTrimming);
        Assert.True(fit.Bounds.Width <= band.Bounds.Width, "the whole name fits across the band");
        Assert.True(fit.Bounds.Width < title.DesiredSize.Width, "a long name is shrunk to fit");
        var bandWithLong = band.Bounds;
        MainWindowTests.Capture(window, "carousel-long-title.png");

        vm.Select(lib.ListEditions().Single(e => e.Title == "Short").Id);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(title.DesiredSize.Width, fit.Bounds.Width, 1); // a short name keeps its full size
        Assert.Equal(bandWithLong, band.Bounds);                   // and the band doesn't move
        window.Close();
    }

    [AvaloniaFact]
    public void Status_line_shows_only_while_an_update_runs()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var (vm, window, _) = Open(lib);
        var status = window.FindControl<TextBlock>("StatusLine")!;
        vm.StatusText = "1 game(s) in library";
        Dispatcher.UIThread.RunJobs();
        Assert.False(status.IsEffectivelyVisible);
        window.Close();
    }
}
