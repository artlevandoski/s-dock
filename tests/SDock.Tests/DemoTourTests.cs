using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SDock.App;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>The --demo-tour (for recording a promo video) runs start to finish and notes each part.</summary>
public sealed class DemoTourTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose()
    {
        SDock.App.Input.GamepadService.NoteKeyboardOrMouse(); // "controller in use" is shared: don't leave it on for other tests
        _env.Dispose();
    }

    [AvaloniaFact]
    public async Task Runs_through_every_part_and_ends_in_the_carousel()
    {
        using var lib = _env.OpenLibrary();
        foreach (var t in new[] { "Lantern Lake", "Maple Hollow", "Moss & Moon", "Neon Harbor", "Orbit Odyssey", "Starfall Drift", "Tidebreaker" })
        {
            var id = lib.AddEdition(t, Generations.Switch1, _env.Game(t + ".nsp"));
            lib.SetFavorite(id, t.StartsWith('M'));
            if (t.StartsWith('S')) lib.RecordPlay(id, TimeSpan.FromHours(3), DateTime.UtcNow);
        }
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { IsCarousel = true };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800, KeepFullScreen = true };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var marks = Path.Combine(_env.Root, "marks.txt");

        await DemoTour.RunAsync(window, vm, marks, speed: 0);

        var parts = File.ReadAllLines(marks).Select(l => l.Split(' ', 2)[1]).ToList();
        Assert.Equal(["browse", "letters", "search", "trailer", "actions", "grid", "tabs", "settings", "finale", "end"], parts);
        Assert.True(vm.IsCarousel);
        Assert.False(vm.IsSettingsOpen || vm.IsMenuOpen || vm.IsTrailerOpen || vm.IsSearchKeyboardOpen);
        Assert.Equal("", vm.SearchText);                             // the search was cleared again
        Assert.False(vm.IsRunning);                                  // nothing was launched
        window.Close();
    }
}
