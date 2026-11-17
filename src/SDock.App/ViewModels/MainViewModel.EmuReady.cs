using CommunityToolkit.Mvvm.Input;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.App.ViewModels;

// EmuReady (emuready.com): community reports of how each game runs on PC emulators. Off by default. When on,
// the game you stop on is looked up (once a week at most) and its rating is shown under Play.
public sealed partial class MainViewModel
{
    public const string EmuReadyKey = "emuready";
    /// <summary>How long a game's rating is kept before it's looked up again.</summary>
    public static readonly TimeSpan EmuReadyMaxAge = TimeSpan.FromDays(7);
    /// <summary>How long the selection must rest on a game before it's looked up (browsing past doesn't ask).</summary>
    public static TimeSpan EmuReadyDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The EmuReady client (tests replace it with one that doesn't go online).</summary>
    public EmuReadyClient EmuReady { get; set; } = new();

    private Dictionary<string, EmuReadySummary> _emuReady = [];
    private readonly HashSet<string> _emuReadyBusy = [];
    private int _emuReadyRequest;

    /// <summary>Settings → Game Info → Show EmuReady ratings.</summary>
    public bool ShowEmuReady
    {
        get => _library.GetSetting(EmuReadyKey) == "1";
        set
        {
            if (value == ShowEmuReady) return;
            _library.SetSetting(EmuReadyKey, value ? "1" : "0");
            OnPropertyChanged();
            NotifyEmuReady();
            if (value) ScheduleEmuReady(SelectedItem);
        }
    }

    private void LoadEmuReady() => _emuReady = _library.AllEmuReady();

    public EmuReadySummary? EmuReadyFor(EditionItem item) => _emuReady.GetValueOrDefault(item.Id);

    /// <summary>The emulator this game plays with, as EmuReady names emulators ("eden"), or null.</summary>
    private string? EmulatorFor(EditionItem item)
    {
        var profile = LaunchRouter.Resolve(_library, item.Edition).Profile;
        return profile is null ? null : profile.Emulator is { Length: > 0 } e && e != "custom" ? e : profile.Name;
    }

    /// <summary>Shown under Play: "EmuReady: Perfect on Eden · 9 PC reports".</summary>
    public string EmuReadyText
    {
        get
        {
            if (!ShowEmuReady || SelectedItem is not { } item || item.Edition.IsProgram) return "";
            if (EmuReadyFor(item) is not { } s)
                return _emuReadyBusy.Contains(item.Id) ? "EmuReady: checking…" : "";
            if (!s.Found) return "EmuReady: not listed yet";
            if (s.For(EmulatorFor(item)) is not { } r) return "EmuReady: no reports yet";
            var count = r.Reports == 1 ? $"1 {s.Source} report" : $"{r.Reports} {s.Source} reports";
            return $"EmuReady: {r.Rating} on {r.Emulator} · {count}";
        }
    }

    public bool HasEmuReadyText => EmuReadyText.Length > 0;

    /// <summary>A tag in the carousel: "EmuReady: Perfect".</summary>
    private string? EmuReadyTag(EditionItem item) =>
        ShowEmuReady && EmuReadyFor(item) is { Found: true } s && s.For(EmulatorFor(item)) is { } r ? $"EmuReady: {r.Rating}" : null;

    /// <summary>Every emulator's rating, for the details panel's tooltip: "Eden: Perfect (9) · Ryujinx: Playable (2)".</summary>
    public string EmuReadyDetails =>
        ShowEmuReady && SelectedItem is { } item && EmuReadyFor(item) is { Found: true } s && s.Emulators.Count > 0
            ? (s.Source == EmuReadySummary.Handheld ? "No PC reports yet; these are from handhelds.\n" : "") +
              string.Join(" · ", s.Emulators.Select(e => $"{e.Emulator}: {e.Rating} ({e.Reports})")) + "\nRatings from emuready.com. Click to open."
            : "Ratings from emuready.com.";

    public bool CanOpenEmuReady => SelectedItem is { } item && EmuReadyFor(item) is { Found: true };

    [RelayCommand]
    private void OpenEmuReady()
    {
        if (SelectedItem is { } item && EmuReadyFor(item)?.Url is { } url) _ui.OpenUrl(url);
    }

    private void NotifyEmuReady()
    {
        OnPropertyChanged(nameof(EmuReadyText));
        OnPropertyChanged(nameof(HasEmuReadyText));
        OnPropertyChanged(nameof(EmuReadyDetails));
        OnPropertyChanged(nameof(CanOpenEmuReady));
        OnPropertyChanged(nameof(SelectedTags));
    }

    /// <summary>After the selection rests on a game: look it up if it has no rating or an old one.</summary>
    private void ScheduleEmuReady(EditionItem? item)
    {
        var request = ++_emuReadyRequest;
        if (!ShowEmuReady || item is null || item.Edition.IsProgram || !NeedsEmuReady(item)) return;
        _ = Task.Delay(EmuReadyDelay).ContinueWith(_ =>
        {
            if (request == _emuReadyRequest && SelectedItem?.Id == item.Id) _ = RefreshEmuReadyAsync(item);
        }, SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool NeedsEmuReady(EditionItem item) =>
        EmuReadyFor(item) is not { } s || DateTime.UtcNow - s.FetchedUtc > EmuReadyMaxAge;

    /// <summary>Looks one game up on EmuReady and keeps the result. Network problems are quiet: it's tried again later.</summary>
    public async Task RefreshEmuReadyAsync(EditionItem item)
    {
        if (!_emuReadyBusy.Add(item.Id)) return;
        if (SelectedItem?.Id == item.Id) NotifyEmuReady();
        try
        {
            var summary = await EmuReady.LookUpAsync(item.Title, item.Generation);
            _library.SetEmuReady(item.Id, summary);
            _emuReady[item.Id] = summary;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline or EmuReady unavailable: show nothing for now.
        }
        finally
        {
            _emuReadyBusy.Remove(item.Id);
            if (SelectedItem?.Id == item.Id) NotifyEmuReady();
        }
    }
}
