using CommunityToolkit.Mvvm.Input;
using SDock.App.Updates;

namespace SDock.App.ViewModels;

// Automatic updates: S-Dock looks for a newer release when it starts (Settings → About can switch that off) and
// asks before updating. An update replaces only the app inside the S-Dock folder; the library and emulators stay.
public sealed partial class MainViewModel
{
    public const string UpdateCheckKey = "update_check";

    /// <summary>Finds and applies updates (tests replace it).</summary>
    public IAppUpdater Updater { get; set; } = new VelopackUpdater();

    private string? _updateVersion;
    private string _updateState = "";
    private bool _updating;

    /// <summary>Settings → About → Check for updates when S-Dock starts (on by default).</summary>
    public bool CheckForUpdatesOnStart
    {
        get => _library.GetSetting(UpdateCheckKey) != "0";
        set
        {
            if (value == CheckForUpdatesOnStart) return;
            _library.SetSetting(UpdateCheckKey, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    /// <summary>For Settings: "Up to date", "0.1.0-beta.3 is available", "Downloading 40%…".</summary>
    public string UpdateStatusLine =>
        !Updater.CanUpdate ? "Only in the released S-Dock folder"
        : _updateState.Length > 0 ? _updateState
        : _updateVersion is { } v ? $"{v} is available"
        : "Not checked yet";

    private void SetUpdateState(string state)
    {
        _updateState = state;
        OnPropertyChanged(nameof(UpdateStatusLine));
        RefreshSettings();
    }

    /// <summary>When S-Dock starts: look quietly, and ask only when there is something newer.</summary>
    public async Task CheckForUpdatesOnStartAsync()
    {
        if (!CheckForUpdatesOnStart || !Updater.CanUpdate) return;
        if (await FindUpdateAsync(quiet: true) is { } version)
            await OfferUpdateAsync(version);
    }

    /// <summary>Settings → About → Check for updates.</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (!Updater.CanUpdate)
        {
            await _ui.ShowMessageAsync("Updates",
                "Updates work in the released S-Dock folder (the one from the download). This copy was started from a build or the source.");
            return;
        }
        if (await FindUpdateAsync(quiet: false) is { } version)
            await OfferUpdateAsync(version);
        else if (_updateState == UpToDate) // (a failed check has already said so)
            await _ui.ShowMessageAsync("Updates", $"S-Dock is up to date ({Updater.CurrentVersion ?? AppVersion}).");
    }

    private const string UpToDate = "Up to date";

    private async Task<string?> FindUpdateAsync(bool quiet)
    {
        if (_updating) return null;
        SetUpdateState("Checking…");
        try
        {
            _updateVersion = await Updater.CheckAsync();
            SetUpdateState(_updateVersion is null ? UpToDate : "");
            return _updateVersion;
        }
        catch (Exception ex)
        {
            SetUpdateState("Couldn't check (offline?)");
            if (!quiet) await _ui.ShowMessageAsync("Updates", $"S-Dock couldn't check for updates.\n\n{ex.Message}");
            return null;
        }
    }

    private async Task OfferUpdateAsync(string version)
    {
        if (Launcher.IsRunning) return; // never in the middle of a game
        var choice = await _ui.ChooseAsync("Update S-Dock", $"S-Dock {version} is available",
            $"You have {Updater.CurrentVersion ?? AppVersion}. S-Dock will download it, close and start again. " +
            "Your library, artwork, settings and emulators are kept.", "Update now", "Later");
        _ui.RestoreFocus();
        if (choice == "Update now") await UpdateNowAsync();
    }

    private async Task UpdateNowAsync()
    {
        if (_updating || Launcher.IsRunning) return;
        _updating = true;
        try
        {
            SetUpdateState("Downloading…");
            await Updater.DownloadAsync(p => Avalonia.Threading.Dispatcher.UIThread.Post(() => SetUpdateState($"Downloading {p}%…")));
            SetUpdateState("Restarting…");
            Updater.ApplyAndRestart(); // closes S-Dock
        }
        catch (Exception ex)
        {
            SetUpdateState("Update failed");
            await _ui.ShowMessageAsync("Update S-Dock", $"The update couldn't be downloaded or applied, so S-Dock stays as it is.\n\n{ex.Message}");
        }
        finally
        {
            _updating = false;
        }
    }
}
