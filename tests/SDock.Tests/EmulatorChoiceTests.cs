using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Choosing a game's emulator: the grid's dropdown, and the carousel's Game Actions.</summary>
public sealed class EmulatorChoiceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (MainViewModel Vm, MainWindow Window, EmulatorProfile Stable, EmulatorProfile Nightly, string Game) Open(bool carousel)
    {
        var lib = _env.OpenLibrary();
        var stable = EmulatorProfile.New("Eden", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        var nightly = EmulatorProfile.New("Eden nightly", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        lib.SaveProfile(stable);
        lib.SaveProfile(nightly);
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        var game = lib.AddEdition("Alpha Quest", Generations.Switch1, _env.Game("A.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { IsCarousel = carousel };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.FocusSelectedCover();
        _libs.Add(lib);
        return (vm, window, stable, nightly, game);
    }

    private readonly List<LibraryStore> _libs = [];

    [AvaloniaFact]
    public void The_grid_dropdown_shows_the_emulator_you_pick()
    {
        var (vm, window, _, nightly, game) = Open(carousel: false);
        window.GetVisualDescendants().OfType<Expander>().Single(x => x.Header as string == "Game settings").IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var combo = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => ReferenceEquals(c.ItemsSource, vm.EmulatorChoices));
        Assert.Equal("Use default (Eden)", (combo.SelectedItem as ProfileChoice)?.Label);

        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs(); combo.SelectedIndex = vm.EmulatorChoices.ToList().FindIndex(c => c.Id == nightly.Id); combo.IsDropDownOpen = false; // pick it as a click would
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Eden nightly", (combo.SelectedItem as ProfileChoice)?.Label); // it stays shown
        Assert.Equal(nightly.Id, _libs[0].GetLaunchOverride(game));
        Assert.Equal("Plays with Eden nightly (this game's choice)", vm.RouteText);

        combo.SelectedItem = vm.EmulatorChoices[0];                // back to the default
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Use default (Eden)", (combo.SelectedItem as ProfileChoice)?.Label);
        Assert.Null(_libs[0].GetLaunchOverride(game));

        // Coming back to the game shows its choice too.
        combo.SelectedItem = vm.EmulatorChoices.Single(c => c.Id == nightly.Id);
        vm.Refresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Eden nightly", (combo.SelectedItem as ProfileChoice)?.Label);
        window.Close();
    }

    [AvaloniaFact]
    public void The_carousel_chooses_with_x_then_emulator()
    {
        var (vm, window, _, nightly, game) = Open(carousel: true);
        Assert.Equal("Eden", vm.EmulatorChoiceLabel);              // the carousel's Emulator button

        GamepadService.Dispatch(window, PadAction.GameActions);   // X
        Dispatcher.UIThread.RunJobs();
        var emulator = vm.MenuItems.Single(m => m.Label == "Emulator: Eden");
        vm.ActivateMenu(emulator);
        Assert.Equal(["Use default (Eden)", "Eden", "Eden nightly", "No emulator (run the game's .exe)", "Emulator Profiles…"],
            vm.MenuItems.Select(m => m.Label));
        Assert.True(vm.MenuItems[0].IsChecked);

        vm.ActivateMenu(vm.MenuItems.Single(m => m.Label == "Eden nightly"));
        Assert.True(vm.IsMenuOpen);                                // a choice applies at once; the page stays
        Assert.True(vm.MenuItems.Single(m => m.Label == "Eden nightly").IsChecked);
        Assert.False(vm.MenuItems[0].IsChecked);
        Assert.Equal(nightly.Id, _libs[0].GetLaunchOverride(game));
        Assert.Equal("Eden nightly", vm.EmulatorChoiceLabel);
        Assert.Equal("Plays with Eden nightly (this game's choice)", vm.RouteText);

        GamepadService.Dispatch(window, PadAction.Cancel);        // B: back to Game Actions, which shows it
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.MenuItems, m => m.Label == "Emulator: Eden nightly");
        GamepadService.Dispatch(window, PadAction.Cancel);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsMenuOpen);

        window.FindControl<Button>("CarouselEmulatorButton")!.Command!.Execute(null); // the mouse: straight to the page
        Assert.True(vm.IsMenuOpen);
        Assert.StartsWith("Emulator for", vm.MenuTitle);
        window.Close();
    }
}

