using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

namespace SwitchYard.Tests;

/// <summary>The settings menu's check and radio items actually change the settings when clicked.</summary>
public sealed class MenuToggleTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private static MenuItem Find(IEnumerable<object?> items, string header) =>
        items.OfType<MenuItem>().Select(m => m.Header as string == header ? m : FindOrNull(m.Items, header))
            .FirstOrDefault(m => m is not null) ?? throw new InvalidOperationException($"No menu item {header}");

    private static MenuItem? FindOrNull(IEnumerable<object?> items, string header)
    {
        try { return Find(items, header); } catch (InvalidOperationException) { return null; }
    }

    /// <summary>Clicks a menu item the way the pointer or keyboard does (MenuItem's own click handling).</summary>
    private static void Click(MenuItem item)
    {
        var onClick = typeof(MenuItem).GetMethod("OnClick", System.Reflection.BindingFlags.Instance |
                                                            System.Reflection.BindingFlags.NonPublic)!;
        onClick.Invoke(item, [new RoutedEventArgs(MenuItem.ClickEvent)]);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Settings_menu_toggles_change_the_settings()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        var button = window.FindControl<Button>("SettingsButton")!;
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.Cast<object?>().ToList();

        var background = Find(items, "Background");
        background.Open();
        Dispatcher.UIThread.RunJobs();
        var color = Find(items, "Color from Game");
        Click(color);
        Assert.Equal(MainViewModel.BackgroundColor, vm.BackgroundStyle);
        Assert.True(color.IsChecked); // the tick follows the setting
        Assert.False(Find(items, "Game Artwork").IsChecked);
        Click(Find(items, "Plain"));
        Assert.Equal(MainViewModel.BackgroundPlain, vm.BackgroundStyle);
        Assert.False(Find(items, "Color from Game").IsChecked);
        Click(Find(items, "Game Artwork"));
        Assert.Equal(MainViewModel.BackgroundArt, vm.BackgroundStyle);

        var playing = Find(items, "Playing");
        playing.Open();
        Dispatcher.UIThread.RunJobs();
        Click(Find(items, "Start Games in Full Screen"));
        Assert.False(vm.StartGamesFullScreen);
        Click(Find(items, "Esc or Start+Select: Press Once to Exit"));
        Assert.True(vm.ExitWithOnePress);
        window.Close();
    }
}
