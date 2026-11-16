using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>
/// Screenshots for the release page, rendered from a library folder. Does nothing unless
/// SDOCK_RELEASE_SHOTS_DATA names a data folder (use a copy; it's opened read-write) and
/// SDOCK_SCREENSHOTS names where to save the pictures. SDOCK_RELEASE_SHOTS_GAME picks the game shown.
///
///   $env:SDOCK_RELEASE_SHOTS_DATA = "...\copy of data"; $env:SDOCK_SCREENSHOTS = "...\shots"
///   dotnet test --filter ReleaseShots
/// </summary>
public sealed class ReleaseShots
{
    [AvaloniaFact]
    public void Release_page_screenshots()
    {
        var data = Environment.GetEnvironmentVariable("SDOCK_RELEASE_SHOTS_DATA");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SDOCK_SCREENSHOTS"))) return;
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirEnv);
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, data);
        try
        {
            using var lib = new LibraryStore(Path.Combine(data, "library.sqlite3"));
            using var launcher = new EmulatorLauncher();
            var vm = new MainViewModel(lib, launcher, new FakeUi()) { IsCarousel = true };
            var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            vm.SelectedLibraryTab = vm.LibraryTabs[0];

            var wanted = Environment.GetEnvironmentVariable("SDOCK_RELEASE_SHOTS_GAME");
            var game = vm.Items.FirstOrDefault(i => wanted is not null && i.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                       ?? vm.Items.Skip(vm.Items.Count / 3).FirstOrDefault(i =>
                              i.Metadata.DisplayLogoFile is not null && i.Metadata.DisplayKeyArtFile is not null)
                       ?? vm.Items[0];
            vm.Select(game.Id);
            vm.IsCarousel = false;                                 // the grid first: it loads the background art
            Settle(window, vm);
            MainWindowTests.Capture(window, "grid.png");

            vm.IsCarousel = true;
            Settle(window, vm, 2500);
            MainWindowTests.Capture(window, "carousel.png");

            vm.BackgroundStyle = MainViewModel.BackgroundColor;
            Settle(window, vm, 2500);
            MainWindowTests.Capture(window, "carousel-color.png");
            vm.BackgroundStyle = MainViewModel.BackgroundArt;
            vm.IsCarousel = false;

            vm.SelectedLibraryTab = vm.LibraryTabs.Single(t => t.Id == Libraries.Favorites);
            if (vm.Items.Count > 0)
            {
                vm.IsCarousel = true;
                Settle(window, vm);
                MainWindowTests.Capture(window, "favorites.png");
                vm.IsCarousel = false;
            }
            vm.SelectedLibraryTab = vm.LibraryTabs[0];
            vm.Select(game.Id);

            vm.OpenSettings();
            vm.SettingsInput(PadAction.Down);                      // Emulators
            vm.SettingsInput(PadAction.Accept);
            Settle(window, vm);
            MainWindowTests.Capture(window, "settings.png");
            vm.CloseSettings();

            vm.OpenGameMenu();
            Settle(window, vm);
            MainWindowTests.Capture(window, "game-actions.png");
            vm.CloseMenu();
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, previous);
        }
    }

    /// <summary>Lets covers, backgrounds and the carousel animation finish before a picture.</summary>
    private static void Settle(Window window, MainViewModel vm, int extraMs = 900)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try { Ui.WaitUntil(() => vm.Items.Take(40).All(i => i.CoverReady) && (vm.SelectedItem?.Metadata.DisplayKeyArtFile is null || vm.SelectedKeyArt is not null), 15000); }
        catch (TimeoutException) { }
        var until = DateTime.UtcNow.AddMilliseconds(extraMs);
        Ui.WaitUntil(() => DateTime.UtcNow > until);
    }
}
