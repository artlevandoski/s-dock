using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.App.Controls;
using SDock.Core.Metadata;

namespace SDock.App.ViewModels;

public sealed partial class ArtChoice(ArtCandidate candidate) : ObservableObject
{
    public ArtCandidate Candidate { get; } = candidate;
    public string Detail => Candidate.Detail;
    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial bool Failed { get; set; }
}

/// <summary>What the user picked in Choose Background… / Choose Logo….</summary>
public sealed record ArtPick(ArtCandidate? Candidate = null, string? File = null, bool UseDefault = false);

/// <summary>Grid of candidate images (with previews) plus "From File…" and "Use Default".</summary>
public sealed partial class ArtPickerViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<List<ArtCandidate>>> _load;
    private readonly IUiServices _ui;
    private readonly CancellationTokenSource _cts = new();

    public ArtKind Kind { get; }
    public string Heading { get; }
    public string Explanation { get; }
    public string EmptyHint { get; }
    public bool IsLogo => Kind == ArtKind.Logo;
    public ObservableCollection<ArtChoice> Choices { get; } = [];
    public event Action<ArtPick?>? CloseRequested;

    public ArtPickerViewModel(ArtKind kind, string title, bool hasSteamGridDb, IUiServices ui,
        Func<CancellationToken, Task<List<ArtCandidate>>> load)
    {
        Kind = kind;
        _ui = ui;
        _load = load;
        Heading = kind switch
        {
            ArtKind.Logo => $"Choose a logo for “{title}”",
            ArtKind.Cover => $"Choose a cover for “{title}”",
            _ => $"Choose a background for “{title}”",
        };
        Explanation = kind switch
        {
            ArtKind.Logo => "Clear logos replace the game name in the carousel and details panel. Your choice is kept when metadata is refreshed.",
            ArtKind.Cover => "Covers are shown square in the grid and carousel; tall covers are trimmed to fit. Your choice is kept when metadata is refreshed.",
            _ => "The background shows behind the carousel. Your choice is kept when metadata is refreshed.",
        };
        EmptyHint = kind == ArtKind.Cover
            ? "No covers found. Look the game up in TitleDB, match it on IGDB" + (hasSteamGridDb ? "" : ", add a SteamGridDB API key") +
              ", or use From File…."
            : kind == ArtKind.Logo
            ? hasSteamGridDb
                ? "Neither IGDB nor SteamGridDB has a logo for this game. Use From File… to add your own."
                : "IGDB has no logo for this game (or it isn't matched yet). A free SteamGridDB API key in " +
                  "Metadata Settings may find one, or use From File…."
            : "No backgrounds found. Match the game on IGDB" + (hasSteamGridDb ? "" : " or add a SteamGridDB API key") +
              ", or use From File….";
    }

    [ObservableProperty] public partial string Status { get; set; } = "Looking for images…";
    [ObservableProperty] public partial bool IsBusy { get; set; } = true;
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    public partial ArtChoice? Selected { get; set; }

    private Task? _started;

    /// <summary>Loads the candidates once; later calls return the same task.</summary>
    public Task StartAsync() => _started ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var candidates = await _load(_cts.Token);
            foreach (var c in candidates) Choices.Add(new ArtChoice(c));
            IsEmpty = Choices.Count == 0;
            Status = IsEmpty ? EmptyHint : $"{Choices.Count} image(s). Pick one and click Use.";
            Selected = Choices.FirstOrDefault();
            IsBusy = false;
            var width = IsLogo ? 320 : 400;
            foreach (var choice in Choices.ToList())
            {
                if (_cts.IsCancellationRequested) break;
                try { choice.Thumbnail = ArtCache.FromBytes(await choice.Candidate.FetchThumb(_cts.Token), width); }
                catch (Exception ex) when (ex is IgdbException or HttpRequestException) { choice.Failed = true; }
                choice.Failed |= choice.Thumbnail is null;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException)
        {
            Status = ex is IgdbException ? ex.Message : "Couldn't reach the image sources. Check your internet connection.";
            IsEmpty = Choices.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanChoose() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void Choose() => Close(new ArtPick(Candidate: Selected!.Candidate));

    [RelayCommand]
    private async Task FromFileAsync()
    {
        var path = await _ui.PickFileAsync(Kind switch
            {
                ArtKind.Logo => "Choose a logo image (transparent PNG works best)",
                ArtKind.Cover => "Choose a cover image (square works best)",
                _ => "Choose a background image",
            },
            null, "Images", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp");
        if (path is not null) Close(new ArtPick(File: path));
    }

    [RelayCommand]
    private void UseDefault() => Close(new ArtPick(UseDefault: true));

    [RelayCommand]
    private void Cancel() => Close(null);

    private void Close(ArtPick? pick)
    {
        _cts.Cancel();
        CloseRequested?.Invoke(pick);
    }
}
