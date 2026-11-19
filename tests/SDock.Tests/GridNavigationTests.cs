using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>The grid with a controller: straight up and down a row, and up into the filters.</summary>
public sealed class GridNavigationTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (MainViewModel Vm, MainWindow Window) Open(LibraryStore lib)
    {
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.FocusSelectedCover();
        return (vm, window);
    }

    private static void Pad(MainWindow window, params PadAction[] actions)
    {
        foreach (var a in actions)
        {
            GamepadService.Dispatch(window, a);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static Control? Focused(Window w) => TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as Control;

    [AvaloniaFact]
    public void Up_and_down_go_straight_along_the_column_and_to_the_last_cover_on_a_short_row()
    {
        using var lib = _env.OpenLibrary();
        for (var i = 0; i < 11; i++) lib.AddEdition($"Game {i:00}", Generations.Switch1, _env.Game($"G{i}.nsp"));
        var (vm, window) = Open(lib);
        var cols = window.GridColumns;
        Assert.True(cols is > 1 and < 11, $"columns: {cols}");

        vm.Select(vm.Items[cols - 1].Id);                            // end of the first row
        window.FocusSelectedCover();
        Pad(window, PadAction.Down);
        Assert.Equal(vm.Items[2 * cols - 1].Id, vm.SelectedItem!.Id); // straight down
        Pad(window, PadAction.Down);                                  // the next row is shorter: to its last cover
        var expected = Math.Min(3 * cols - 1, vm.Items.Count - 1);
        Assert.Equal(vm.Items[expected].Id, vm.SelectedItem!.Id);
        Pad(window, PadAction.Up);
        Assert.Equal(vm.Items[expected - cols].Id, vm.SelectedItem!.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void Up_from_the_top_row_reaches_the_filters_and_they_work_with_the_controller()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.AddEdition("Bravo", Generations.Switch2, _env.Game("B.nsp"));
        var (vm, window) = Open(lib);

        Pad(window, PadAction.Up);                                    // top row: up into the filters
        Assert.IsType<ListBoxItem>(Focused(window));                  // on All Games
        Pad(window, PadAction.Right);                                 // on Switch 1: it's highlighted before choosing
        var onPill = Assert.IsType<ListBoxItem>(Focused(window));
        Assert.Equal("Switch 1", (onPill.DataContext as FilterOption)?.Label);
        Assert.Contains(":focus-visible", onPill.Classes);            // the bright ring (App.axaml)
        Assert.Equal(FilterOption.AllValue, vm.GenerationFilter.Value); // not chosen yet
        Pad(window, PadAction.Accept);                                // chosen with A
        Assert.Equal(Generations.Switch1, vm.GenerationFilter.Value);
        Assert.Equal(["Alpha"], vm.Items.Select(i => i.Title));

        Pad(window, PadAction.Right, PadAction.Right);                // past Switch 2 to More filters
        Assert.Equal("MoreFilterCombo", Focused(window)?.Name);
        Pad(window, PadAction.Accept);                                // A opens it
        var more = window.FindControl<ComboBox>("MoreFilterCombo")!;
        Assert.True(more.IsDropDownOpen);
        Pad(window, PadAction.Down, PadAction.Down, PadAction.Down, PadAction.Accept); // to Not in TitleDB; A closes it
        Assert.False(more.IsDropDownOpen);
        Assert.Equal(FilterOption.NotInTitleDbValue, vm.GenerationFilter.Value);

        Pad(window, PadAction.Cancel);                                // B: back to the covers
        Assert.IsType<EditionItem>(Assert.IsType<ListBoxItem>(Focused(window)).DataContext); // a cover again
        window.Close();
    }

    [AvaloniaFact]
    public void A_close_request_right_after_an_exit_press_is_ignored_but_quitting_still_works()
    {
        using var lib = _env.OpenLibrary();
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        Assert.False(vm.JustPressedExit);
        vm.NoteExitPress();                                           // Start + Select (or Esc) during a game
        Assert.True(vm.JustPressedExit);                              // Alt+F4 from Steam Input now is ignored
        var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();                                               // S-Dock's own Quit is not an outside request
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task Rename_changes_the_name_shown_sorted_and_searched_and_looks_it_up_again()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Kirby™ and the Forgotten Land", Generations.Switch1, _env.Game("K.nsp"));
        lib.SetEmuReady(a, SDock.Core.Metadata.EmuReadySummary.NotFound(DateTime.UtcNow));
        var ui = new FakeUi { NextEditedText = "Kirby and the Forgotten Land" };
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        vm.OpenGameMenu();
        Assert.Contains(vm.MenuItems, m => m.Label == "Rename…");
        vm.CloseMenu();

        await vm.RenameCommand.ExecuteAsync(null);
        Assert.Equal("Kirby and the Forgotten Land", lib.GetEdition(a)!.Title);
        Assert.Equal("Kirby and the Forgotten Land", vm.SelectedTitle);
        Assert.False(lib.AllEmuReady().ContainsKey(a));                // asked again under the new name
    }
}
