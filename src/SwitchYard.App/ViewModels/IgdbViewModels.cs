using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.App.Controls;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.App.ViewModels;

/// <summary>IGDB Settings: the user's own Twitch application credentials, with Test Connection.</summary>
public sealed partial class IgdbSettingsViewModel : ObservableObject
{
    public const string TwitchConsoleUrl = "https://dev.twitch.tv/console/apps";
    public const string IgdbDocsUrl = "https://api-docs.igdb.com/#account-creation";

    private readonly IUiServices _ui;
    private readonly Func<string, string, IgdbClient> _clientFactory;

    public event Action<bool>? CloseRequested;

    public IgdbSettingsViewModel(string clientId, string clientSecret, IUiServices ui, Func<string, string, IgdbClient> clientFactory)
    {
        _ui = ui;
        _clientFactory = clientFactory;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    public string Explanation =>
        "Switch Yard fetches artwork and descriptions from IGDB using your own free Twitch developer " +
        "credentials. Register an application in the Twitch developer console (any name; OAuth redirect " +
        "http://localhost; category: Application Integration; client type: Confidential), then paste its " +
        "Client ID and a new Client Secret here.";

    public string StorageNote =>
        "Stored only in Switch Yard's data folder on this computer, never in the app or the git repository.";

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(TestCommand))] public partial string ClientId { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(TestCommand))] public partial string ClientSecret { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(TestCommand))] public partial bool IsTesting { get; set; }

    private bool CanTest() => !IsTesting && ClientId.Trim().Length > 0 && ClientSecret.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        IsTesting = true;
        Status = "Connecting to IGDB…";
        StatusIsError = false;
        try
        {
            using var client = _clientFactory(ClientId, ClientSecret);
            await client.TestAsync();
            Status = "Connected. IGDB is ready; click Save.";
        }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException or TaskCanceledException)
        {
            Status = ex is IgdbException ? ex.Message : "Couldn't reach IGDB. Check your internet connection.";
            StatusIsError = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand] private void OpenTwitchConsole() => _ui.OpenUrl(TwitchConsoleUrl);
    [RelayCommand] private void OpenIgdbDocs() => _ui.OpenUrl(IgdbDocsUrl);
    [RelayCommand] private void Save() => CloseRequested?.Invoke(true);
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke(false);
}

public sealed partial class MatchCandidate(IgdbGame game, bool isCurrent, bool onPlatform) : ObservableObject
{
    public IgdbGame Game { get; } = game;
    public string Name => Game.Name;
    public bool IsCurrent { get; } = isCurrent;

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Game.ReleaseYear is { } y) parts.Add(y.ToString());
            var platforms = Game.PlatformNames.Take(4).ToList();
            if (platforms.Count > 0) parts.Add(string.Join(", ", platforms));
            if (!onPlatform) parts.Add("not listed for this generation");
            if (IsCurrent) parts.Add("current match");
            return string.Join("  ·  ", parts);
        }
    }

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
}

/// <summary>Match on IGDB… / Change Match…: editable search, result previews, or a pasted IGDB URL / ID.</summary>
public sealed partial class MatchViewModel : ObservableObject
{
    private readonly Edition _edition;
    private readonly MetadataService _service;
    private CancellationTokenSource? _searchCts;

    public ObservableCollection<MatchCandidate> Results { get; } = [];
    public event Action<IgdbGame?>? CloseRequested;

    public MatchViewModel(Edition edition, MetadataService service)
    {
        _edition = edition;
        _service = service;
        Query = edition.Title;
    }

    public string Heading => _edition.IgdbId is null ? $"Match “{_edition.Title}” on IGDB" : $"Change IGDB match for “{_edition.Title}”";

    public string PlatformNote => _edition.Generation switch
    {
        Generations.Switch1 => "Searching Nintendo Switch games.",
        Generations.Switch2 => "Searching Nintendo Switch 2 games.",
        _ => "This game is Unclassified, so both Switch platforms are searched.",
    };

    [ObservableProperty] public partial string Query { get; set; }
    [ObservableProperty] public partial bool AnyPlatform { get; set; }
    [ObservableProperty] public partial string Reference { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand))]
    public partial MatchCandidate? Selected { get; set; }

    partial void OnAnyPlatformChanged(bool value) => _ = SearchAsync();

    /// <summary>Runs the first search when the dialog opens.</summary>
    public Task StartAsync() => SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        IsBusy = true;
        Status = "Searching IGDB…";
        try
        {
            var games = await _service.SearchAsync(Query, _edition.Generation, AnyPlatform, cts.Token);
            if (cts.IsCancellationRequested) return;
            var platforms = await _service.PlatformsForAsync(_edition.Generation, cts.Token);
            ShowResults(games, platforms);
            Status = games.Count == 0
                ? AnyPlatform ? "No results. Try a shorter title, or paste an IGDB link." : "No results on this platform. Try “Search all platforms” or a shorter title."
                : $"{games.Count} result(s). Pick the right game and click Match.";
            await LoadThumbnailsAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException)
        {
            Status = ex is IgdbException ? ex.Message : "Couldn't reach IGDB. Check your internet connection.";
        }
        finally
        {
            if (_searchCts == cts) IsBusy = false;
        }
    }

    private void ShowResults(IEnumerable<IgdbGame> games, IReadOnlyCollection<long> platforms)
    {
        Results.Clear();
        foreach (var g in games)
            Results.Add(new MatchCandidate(g, g.Id == _edition.IgdbId, g.Platforms?.Any(p => platforms.Contains(p.Id)) ?? false));
        Selected = Results.FirstOrDefault(r => r.IsCurrent)
                   ?? (MetadataService.PickConfident(_edition.Title, [.. platforms], Results.Select(r => r.Game).ToList()) is { } best
                       ? Results.First(r => r.Game.Id == best.Id)
                       : Results.FirstOrDefault());
    }

    private async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        foreach (var r in Results.ToList())
        {
            if (ct.IsCancellationRequested || r.Game.Cover?.ImageId is not { Length: > 0 } id) continue;
            try
            {
                var bytes = await _service.Client.DownloadImageAsync(id, MetadataService.ThumbSize, ct);
                r.Thumbnail = ArtCache.FromBytes(bytes, 90);
            }
            catch (Exception ex) when (ex is IgdbException or HttpRequestException) { } // previews are optional
        }
    }

    [RelayCommand]
    private async Task LookUpAsync()
    {
        if (IgdbClient.ParseReference(Reference) == (null, null))
        {
            Status = "Paste an IGDB game link (igdb.com/games/…), its slug, or its numeric ID.";
            return;
        }
        IsBusy = true;
        try
        {
            var game = await _service.Client.GetByReferenceAsync(Reference);
            if (game is null)
            {
                Status = "IGDB has no game at that link or ID.";
                return;
            }
            ShowResults([game], await _service.PlatformsForAsync(_edition.Generation));
            Status = $"Found {game.Name}. Click Match to use it.";
            await LoadThumbnailsAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException)
        {
            Status = ex is IgdbException ? ex.Message : "Couldn't reach IGDB. Check your internet connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanMatch() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanMatch))]
    private void Match() => CloseRequested?.Invoke(Selected?.Game);

    [RelayCommand]
    private void Cancel()
    {
        _searchCts?.Cancel();
        CloseRequested?.Invoke(null);
    }
}
