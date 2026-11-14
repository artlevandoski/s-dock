using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.App.ViewModels;

// Updates and DLC: the latest known update (nx-versions) against the update files beside each game.
public sealed partial class MainViewModel
{
    public const string NxVersionsKey = "nx_versions";

    /// <summary>
    /// Settings → Use nx-versions: supplement TitleDB with the latest known update versions and first-seen dates.
    /// Turning it off stops refreshing and using it; versions already recorded stay.
    /// </summary>
    public bool UseNxVersions
    {
        get => _library.GetSetting(NxVersionsKey) != "0";
        set
        {
            if (value == UseNxVersions) return;
            _library.SetSetting(NxVersionsKey, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleNxVersions() => UseNxVersions = !UseNxVersions;

    private Dictionary<string, InstalledContent> _installed = [];
    private int _installedScan;

    /// <summary>Re-reads the update and DLC files beside every game, in the background.</summary>
    public void ScanInstalledContent()
    {
        var editions = _all.Select(i => i.Edition).ToList();
        var scan = ++_installedScan;
        _ = Task.Run(() => LocalContent.Index(editions)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && scan == _installedScan) InstalledContentReady(t.Result);
        }, SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>For tests and the first load: use this index now.</summary>
    public void InstalledContentReady(Dictionary<string, InstalledContent> index)
    {
        _installed = index;
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(SelectedTags));
        if (GenerationFilter?.Value == FilterOption.UpdateAvailableValue) ApplyFilter();
    }

    public InstalledContent? InstalledFor(EditionItem item) => _installed.GetValueOrDefault(item.Id);

    /// <summary>True when nx-versions knows a newer update than the newest one beside the game.</summary>
    public bool HasUpdateAvailable(EditionItem item) =>
        item.Metadata.NxLatestUpdate is { } latest && latest > (InstalledFor(item)?.UpdateVersion ?? 0);

    /// <summary>"Update available: update 4 (v262144), you have update 2" / "Up to date: update 4 (v262144)".</summary>
    public string UpdateStatusText => SelectedItem is { } item ? UpdateStatus(item) : "";

    private string UpdateStatus(EditionItem item)
    {
        var installed = InstalledFor(item);
        var have = installed?.UpdateVersion;
        var parts = new List<string>();
        if (item.Metadata.NxLatestUpdate is { } latest)
        {
            if (have >= latest) parts.Add($"Up to date: {NxVersions.Describe(have.Value)}");
            else
            {
                var when = item.Metadata.NxUpdateDate is { } d ? $", out {d / 10000}-{d / 100 % 100:00}-{d % 100:00}" : "";
                parts.Add($"Update available: {NxVersions.Describe(latest)}{when}" +
                          (have is { } h ? $" (you have {NxVersions.Describe(h)})" : " (none installed)"));
            }
        }
        else if (have is { } h)
            parts.Add($"Update installed: {NxVersions.Describe(h)}");
        if (installed is { DlcCount: > 0 } i) parts.Add(i.DlcCount == 1 ? "1 DLC" : $"{i.DlcCount} DLC");
        return string.Join("  ·  ", parts);
    }
}
