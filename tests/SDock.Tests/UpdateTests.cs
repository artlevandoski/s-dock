using Avalonia.Headless.XUnit;
using SDock.App.Updates;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

public sealed class FakeUpdater : IAppUpdater
{
    public bool CanUpdate { get; set; } = true;
    public string? CurrentVersion { get; set; } = "0.1.0-beta.2";
    public string? Newer { get; set; }
    public bool Offline { get; set; }
    public int Checks, Downloads, Restarts;

    public Task<string?> CheckAsync(CancellationToken ct = default)
    {
        Checks++;
        if (Offline) throw new HttpRequestException("No connection");
        return Task.FromResult(Newer);
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken ct = default)
    {
        Downloads++;
        progress(100);
        return Task.CompletedTask;
    }

    public void ApplyAndRestart() => Restarts++;
    public void ApplyAndExit() => Restarts++;
}

/// <summary>Automatic updates: checked on start, asked before applying, never during a game.</summary>
public sealed class UpdateTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (MainViewModel Vm, FakeUi Ui, FakeUpdater Updater) Make(LibraryStore lib, string? newer)
    {
        var ui = new FakeUi();
        var updater = new FakeUpdater { Newer = newer };
        return (new MainViewModel(lib, new EmulatorLauncher(), ui) { Updater = updater }, ui, updater);
    }

    [AvaloniaFact]
    public async Task On_start_it_asks_and_updates_only_when_you_say_so()
    {
        using var lib = _env.OpenLibrary();
        var (vm, ui, updater) = Make(lib, "0.1.0-beta.3");
        ui.ChooseButton = "Later";
        await vm.CheckForUpdatesOnStartAsync();
        Assert.Equal(1, updater.Checks);
        Assert.Equal(0, updater.Downloads);                        // Later: nothing happens
        Assert.Equal("0.1.0-beta.3 is available", vm.UpdateStatusLine);

        ui.ChooseButton = "Update now";
        await vm.CheckForUpdatesOnStartAsync();
        Assert.Equal((1, 1), (updater.Downloads, updater.Restarts));
    }

    [AvaloniaFact]
    public async Task Up_to_date_or_switched_off_asks_nothing()
    {
        using var lib = _env.OpenLibrary();
        var (vm, ui, updater) = Make(lib, null);
        await vm.CheckForUpdatesOnStartAsync();
        Assert.Equal("Up to date", vm.UpdateStatusLine);
        Assert.Empty(ui.Messages);

        vm.CheckForUpdatesOnStart = false;                         // Settings → About
        await vm.CheckForUpdatesOnStartAsync();
        Assert.Equal(1, updater.Checks);
        Assert.False(new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()).CheckForUpdatesOnStart); // remembered
    }

    [AvaloniaFact]
    public async Task Check_by_hand_says_up_to_date_offline_or_not_the_released_folder()
    {
        using var lib = _env.OpenLibrary();
        var (vm, ui, updater) = Make(lib, null);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("up to date", ui.Messages.Last());

        updater.Offline = true;
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("couldn't check", ui.Messages.Last());
        Assert.Equal("Couldn't check (offline?)", vm.UpdateStatusLine);

        updater.CanUpdate = false;                                 // run from source or a plain build
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("released S-Dock folder", ui.Messages.Last());
        Assert.Equal("Only in the released S-Dock folder", vm.UpdateStatusLine);
    }

    [AvaloniaFact]
    public void Settings_about_has_the_update_items()
    {
        using var lib = _env.OpenLibrary();
        var (vm, _, _) = Make(lib, null);
        vm.OpenSettings();
        vm.SettingsSectionIndex = vm.SettingsSections.ToList().FindIndex(s => s.Title == "About");
        Assert.Contains(vm.SettingsRows, r => r.Label == "Check for updates" && r.ValueText == "Not checked yet");
        Assert.Contains(vm.SettingsRows, r => r.Label == "Check for updates when S-Dock starts" && r.On);
    }
}
