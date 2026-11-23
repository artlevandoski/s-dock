using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>The controller in every kind of dialog: messages, choosers, pickers, text editors and settings-style forms.</summary>
public sealed class DialogControllerTests : IDisposable
{
    private readonly TestEnv _env = new();
    // These drive dialogs the classic way (D-pad between controls, A presses): no on-screen keyboard in the way.
    public DialogControllerTests() => SDock.App.Input.GamepadService.NoteKeyboardOrMouse();
    public void Dispose() => _env.Dispose();

    private static Window Owner()
    {
        var owner = new Window { Width = 1000, Height = 700 };
        owner.Show();
        return owner;
    }

    /// <summary>Opens a dialog the way S-Dock does, and waits until it's ready for the controller.</summary>
    private static Task<T?> Open<T>(Window dialog, Window owner)
    {
        DialogNavigation.Attach(dialog);
        var result = dialog.ShowDialog<T?>(owner);
        Ui.WaitUntil(() => dialog.IsVisible);
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static void Press(Window dialog, params PadAction[] actions)
    {
        foreach (var a in actions)
        {
            GamepadService.Dispatch(dialog, a);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Control? Focused(Window w) => TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as Control;

    [AvaloniaFact]
    public async Task Yes_no_question()
    {
        var owner = Owner();
        var dialog = new MessageWindow("Question", null, "Do it?", ["Yes", "No"]);
        var result = Open<string>(dialog, owner);
        Assert.Equal("Yes", (Focused(dialog) as Button)?.Content);  // the main button has the focus at once
        Press(dialog, PadAction.Right);
        Assert.Equal("No", (Focused(dialog) as Button)?.Content);
        Press(dialog, PadAction.Accept);
        Assert.Equal("No", await result);

        var second = new MessageWindow("Question", null, "Do it?", ["Yes", "No"]);
        var closed = Open<string>(second, owner);
        Press(second, PadAction.Cancel);                              // B: no answer
        Assert.Null(await closed);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Choose_cover_with_the_dpad()
    {
        var owner = Owner();
        var candidates = Enumerable.Range(1, 3).Select(i => new ArtCandidate($"Image {i}", 512, 512, ".png",
            _ => Task.FromResult(FakeIgdb.TinyPng), _ => Task.FromResult(FakeIgdb.TinyPng))).ToList();
        var vm = new ArtPickerViewModel(ArtKind.Cover, "Game", false, new FakeUi(), _ => Task.FromResult(candidates));
        var dialog = new ArtPickerWindow(vm);
        Assert.Equal("Choose Cover", dialog.Title);
        var result = Open<ArtPick>(dialog, owner);
        Ui.WaitUntil(() => vm.Choices.Count == 3 && Focused(dialog) is ListBoxItem);

        Press(dialog, PadAction.Right);
        Assert.Equal("Image 2", vm.Selected?.Candidate.Source);
        Press(dialog, PadAction.Accept);                              // A on a list item: Use
        Assert.Equal("Image 2", (await result)?.Candidate?.Source);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Text_editor_saves_or_cancels()
    {
        var owner = Owner();
        var dialog = new TextEditWindow("Sort Title", "Explain", "Zelda 1");
        var result = Open<string>(dialog, owner);
        Assert.IsType<TextBox>(Focused(dialog));                      // ready for a keyboard
        Press(dialog, PadAction.Accept);                              // A saves (Enter would add a new line)
        Assert.Equal("Zelda 1", await result);

        var again = new TextEditWindow("Sort Title", "Explain", "Zelda 1");
        var cancelled = Open<string>(again, owner);
        Ui.WaitUntil(() => Focused(again) is TextBox);
        Press(again, PadAction.Down);                                 // out of the text box to Save…
        Assert.Equal("Save", (Focused(again) as Button)?.Content);
        Press(again, PadAction.Right);                                // …then Cancel
        Assert.Equal("Cancel", (Focused(again) as Button)?.Content);
        Press(again, PadAction.Accept);
        Assert.Null(await cancelled);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Emulator_picker_with_the_dpad()
    {
        var owner = Owner();
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Game", Generations.Switch1, _env.Game());
        var a = _env.FakeProfile() with { Name = "Eden A" };
        var b = EmulatorProfile.New("Eden B", a.Executable, a.Args);
        var vm = new ProfilePickerViewModel(lib.GetEdition(id)!, [a, b], oneTime: true);
        var dialog = new ProfilePickerWindow(vm);
        var result = Open<bool>(dialog, owner);
        Assert.IsType<ListBoxItem>(Focused(dialog));
        Press(dialog, PadAction.Down, PadAction.Accept);
        Assert.True(await result);
        Assert.Equal("Eden B", vm.Selected?.Profile.Name);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Forms_with_dropdowns_and_check_boxes()
    {
        var owner = Owner();
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 0 };
        var check = new CheckBox { Content = "Option" };
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window { Width = 400, Height = 300 };
        ok.Click += (_, _) => dialog.Close("ok");
        cancel.Click += (_, _) => dialog.Close("cancel");
        dialog.Content = new StackPanel { Children = { combo, check, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { ok, cancel } } } };
        var result = Open<string>(dialog, owner);
        Assert.Same(ok, Focused(dialog));

        Press(dialog, PadAction.Up, PadAction.Up);                    // up through the form to the dropdown
        Assert.Same(combo, Focused(dialog));
        Press(dialog, PadAction.Accept);                              // A opens it
        Assert.True(combo.IsDropDownOpen);
        Press(dialog, PadAction.Down, PadAction.Down, PadAction.Accept); // pick, then keep the pick
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal("Three", combo.SelectedItem);

        Press(dialog, PadAction.Down, PadAction.Accept);              // A ticks the check box
        Assert.True(check.IsChecked);
        Press(dialog, PadAction.Cancel);                              // B: Cancel
        Assert.Equal("cancel", await result);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task From_the_game_menu_into_a_dialog_and_back()
    {
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        lib.SaveMetadata(EditionMetadata.Empty(id) with { TdbSummary = "First sentence. Second." });
        var window = new MainWindow { Width = 1280, Height = 800 };
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new UiServices(window));
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.Select(id);

        GamepadService.Dispatch(window, PadAction.GameActions);      // X
        Dispatcher.UIThread.RunJobs();
        var list = window.FindControl<ListBox>("MenuList")!;
        var index = vm.MenuItems.ToList().FindIndex(m => m.Label == "Edit Carousel Text…");
        while (list.SelectedIndex < index) { GamepadService.Dispatch(window, PadAction.Down); Dispatcher.UIThread.RunJobs(); }
        GamepadService.Dispatch(window, PadAction.Accept);           // opens the text editor dialog
        Ui.WaitUntil(() => window.OwnedWindows.Count == 1);
        var dialog = window.OwnedWindows[0];
        Ui.WaitUntil(() => Focused(dialog) is TextBox);

        GamepadService.Dispatch(dialog, PadAction.Accept);           // A: Save
        Ui.WaitUntil(() => window.OwnedWindows.Count == 0);
        Ui.WaitUntil(() => vm.StatusText.Contains("first sentence"));
        Assert.False(vm.IsMenuOpen);
        await Task.CompletedTask;
        window.Close();
    }
}
