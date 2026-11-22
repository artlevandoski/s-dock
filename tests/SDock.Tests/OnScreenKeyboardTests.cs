using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App.Controls;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>The on-screen keyboard: typing with the controller in text dialogs and the grid's search.</summary>
public sealed class OnScreenKeyboardTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private static (OnScreenKeyboard Keyboard, TextBox Box, Window Window) Keyboard(string text = "")
    {
        var box = new TextBox { Text = text };
        var keyboard = new OnScreenKeyboard { Target = box };
        var window = new Window { Content = new StackPanel { Children = { box, keyboard } }, Width = 900, Height = 600 };
        window.Show();
        box.Focus();
        box.CaretIndex = text.Length;
        return (keyboard, box, window);
    }

    private static void Press(OnScreenKeyboard k, params PadAction[] actions)
    {
        foreach (var a in actions) Assert.True(k.Input(a));
    }

    [AvaloniaFact]
    public void Types_with_the_dpad_and_a_with_one_capital_after_shift()
    {
        var (k, box, window) = Keyboard();
        Assert.Equal("q", k.HighlightedKey);                         // starts on the letters
        Press(k, PadAction.ToggleView);                              // Y: shift
        Assert.Equal("Q", k.HighlightedKey);
        Press(k, PadAction.Right, PadAction.Accept);                 // W
        Press(k, PadAction.Right, PadAction.Accept);                 // e (shift was for one letter)
        Press(k, PadAction.GameActions);                             // X: space
        Press(k, PadAction.Down, PadAction.Accept);                  // the key under it: d
        Assert.Equal("We d", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void B_deletes_then_closes_and_start_is_done()
    {
        var (k, box, window) = Keyboard("ab");
        var closed = 0; var done = 0;
        k.Closed += () => closed++;
        k.Done += () => done++;
        Press(k, PadAction.Cancel);                                  // B: delete
        Assert.Equal("a", box.Text);
        Press(k, PadAction.Cancel, PadAction.Cancel);                // the last letter, then nothing left: close
        Assert.Equal("", box.Text);
        Assert.Equal(1, closed);
        Press(k, PadAction.Play);                                    // Start: done
        Assert.Equal(1, done);
        window.Close();
    }

    [AvaloniaFact]
    public void The_cursor_moves_and_symbols_and_the_bottom_row_work()
    {
        var (k, box, window) = Keyboard("Zelda");
        Press(k, PadAction.PreviousLibrary, PadAction.PreviousLibrary); // LB twice: before "da"
        Press(k, PadAction.GameActions);                               // space in the middle
        Assert.Equal("Zel da", box.Text);
        Press(k, PadAction.Menu);                                      // Select: symbols
        Press(k, PadAction.Accept);                                    // the key on the symbols row: !
        Assert.Equal("Zel !da", box.Text);
        Press(k, PadAction.Up, PadAction.Up);                          // row 1 → 0 → the bottom row (wraps)
        Assert.Contains(k.HighlightedKey, new[] { "⇧ Shift", "ABC", "Space", "⌫", "Done" });
        window.Close();
    }

    [AvaloniaFact]
    public void The_text_editor_shows_it_for_the_controller_and_start_saves()
    {
        GamepadService.NoteController();                              // the controller was used last
        var editor = new TextEditWindow("Rename", "", "Kirby");
        editor.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.Keyboard.IsVisible);
        GamepadService.Dispatch(editor, PadAction.Cancel);           // B deletes, through the normal controller path
        Assert.Equal("Kirb", editor.Keyboard.Target!.Text);
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        GamepadService.Dispatch(editor, PadAction.Play);             // Start: saves and closes
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);

        GamepadService.NoteKeyboardOrMouse();                         // typed on a real keyboard last
        var quiet = new TextEditWindow("Rename", "", "x");
        quiet.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(quiet.Keyboard.IsVisible);                      // stays out of the way
        quiet.Close();
    }

    [AvaloniaFact]
    public void Up_in_the_carousel_searches_with_the_keyboard_and_the_box_stays_while_filtering()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { IsCarousel = true };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.FocusSelectedCover();
        Assert.False(vm.ShowSearchBox);                              // clean: just a search button
        Assert.True(window.FindControl<Button>("SearchButton")!.IsEffectivelyVisible);

        GamepadService.Dispatch(window, PadAction.Up);               // Up: search
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSearchKeyboardOpen);
        Assert.True(vm.ShowSearchBox);
        Assert.True(vm.IsCarousel);                                  // still in the carousel
        OnScreenKeyboard.In(window)!.Type("alp");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Alpha"], vm.Items.Select(i => i.Title));
        GamepadService.Dispatch(window, PadAction.Play);             // Start: done
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsSearchKeyboardOpen);
        Assert.True(vm.ShowSearchBox);                               // the search still filters, so the box stays
        vm.SearchText = "";
        Assert.False(vm.ShowSearchBox);                              // cleared: clean again
        window.Close();
    }

    [AvaloniaFact]
    public void Grid_search_from_the_filters_filters_as_you_type()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.FocusSelectedCover();

        void Pad(PadAction a) { GamepadService.Dispatch(window, a); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Pad(PadAction.Up);                                           // covers → filters
        Pad(PadAction.Up);                                           // filters → search, with the keyboard
        Assert.True(vm.IsSearchKeyboardOpen);
        var keyboard = OnScreenKeyboard.In(window)!;
        keyboard.Type("bra");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Bravo"], vm.Items.Select(i => i.Title));      // filtered as you type
        Pad(PadAction.Play);                                         // Start: done, to the results
        Assert.False(vm.IsSearchKeyboardOpen);
        Assert.Equal("Bravo", vm.SelectedTitle);
        Assert.Equal("bra", vm.SearchText);
        window.Close();
    }
}
