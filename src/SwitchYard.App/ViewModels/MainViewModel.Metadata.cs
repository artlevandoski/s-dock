using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.App.Controls;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.App.ViewModels;

/// <summary>IGDB metadata and artwork: details, matching, user edits and Match Unmatched Games.</summary>
public sealed partial class MainViewModel
{
    private readonly Func<LibraryStore, IgdbClient?> _igdbFactory;
    private IgdbClient? _igdb;
    private (string, string) _igdbCredentials;
    private CancellationTokenSource? _matchAllCts;

    private static IgdbClient? DefaultIgdbFactory(LibraryStore library) =>
        library.HasIgdbCredentials ? new IgdbClient(library.IgdbCredentials.ClientId, library.IgdbCredentials.ClientSecret) : null;

    /// <summary>The IGDB client for the current credentials (recreated when they change).</summary>
    private IgdbClient? Igdb
    {
        get
        {
            var creds = _library.IgdbCredentials;
            if (_igdb is null || creds != _igdbCredentials)
            {
                _igdb?.Dispose();
                _igdb = _igdbFactory(_library);
                _igdbCredentials = creds;
            }
            return _igdb;
        }
    }

    private MetadataService? Metadata => Igdb is { } client ? new MetadataService(_library, client, AppPaths.ArtworkDir) : null;

    /// <summary>Returns the metadata service, or offers IGDB Settings when no credentials are set.</summary>
    private async Task<MetadataService?> RequireMetadataAsync()
    {
        if (Metadata is { } service) return service;
        if (await _ui.ConfirmAsync("IGDB", "To fetch artwork and descriptions, Switch Yard needs your own free IGDB (Twitch) " +
                                           "developer credentials. Open IGDB Settings now?"))
            await IgdbSettingsAsync();
        return Metadata;
    }

    private string ArtPath(string? file) => file is null ? "" : Path.Combine(AppPaths.ArtworkDir, file);

    // ---------------------------------------------------------------- details
    public bool IsMatched => SelectedItem?.IsMatched ?? false;
    public Bitmap? SelectedCover => SelectedItem?.Cover;
    public Bitmap? SelectedKeyArt => SelectedItem?.Metadata.DisplayKeyArtFile is { } f ? ArtCache.Load(ArtPath(f), 1280) : null;
    public Bitmap? SelectedLogo => SelectedItem?.Metadata.DisplayLogoFile is { } f ? ArtCache.Load(ArtPath(f), 600) : null;
    public bool HasLogo => SelectedLogo is not null;
    public string SelectedSummary => SelectedItem?.Metadata.DisplaySummary ?? "";
    public bool HasSummary => SelectedSummary.Length > 0;

    /// <summary>"Platformer · Adventure · 2023".</summary>
    public string SelectedFactsLine
    {
        get
        {
            if (SelectedItem?.Metadata is not { } m) return "";
            var parts = m.Genres.Take(3).ToList();
            if (m.ReleaseYear is { } y) parts.Add(y.ToString());
            return string.Join("  ·  ", parts);
        }
    }

    public string MatchStatusText => SelectedItem switch
    {
        null => "",
        { IsMatched: true, Metadata.IgdbName: { } name } => $"IGDB: {name}",
        { IsMatched: true } => "IGDB: matched",
        _ => "Not matched on IGDB",
    };

    public string MatchMenuLabel => IsMatched ? "Change Match…" : "Match on IGDB…";

    // ---------------------------------------------------------------- settings
    [RelayCommand]
    private async Task IgdbSettingsAsync()
    {
        var (id, secret) = _library.IgdbCredentials;
        var vm = new IgdbSettingsViewModel(id, secret, _ui, (i, s) => new IgdbClient(i, s));
        if (await _ui.ShowIgdbSettingsAsync(vm))
        {
            _library.IgdbCredentials = (vm.ClientId, vm.ClientSecret);
            StatusText = _library.HasIgdbCredentials ? "IGDB credentials saved." : "IGDB credentials cleared.";
        }
        _ui.RestoreFocus();
    }

    // ---------------------------------------------------------------- matching
    [RelayCommand]
    private async Task MatchOnIgdbAsync()
    {
        if (SelectedItem?.Edition is not { } e) return;
        if (await RequireMetadataAsync() is not { } service) return;
        var vm = new MatchViewModel(e, service);
        var game = await _ui.ShowMatchAsync(vm);
        if (game is null) { _ui.RestoreFocus(); return; }
        StatusText = $"Downloading artwork for {game.Name}…";
        try
        {
            await service.ApplyMatchAsync(e.Id, game);
            Refresh(e.Id);
            StatusText = $"Matched “{e.Title}” to {game.Name} on IGDB.";
        }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException or IOException)
        {
            await _ui.ShowMessageAsync("Match on IGDB", "The match couldn't be saved:\n\n" + ex.Message);
        }
        _ui.RestoreFocus();
    }

    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        if (SelectedItem?.Edition is not { IgdbId: not null } e) return;
        if (await RequireMetadataAsync() is not { } service) return;
        try
        {
            StatusText = $"Refreshing “{e.Title}” from IGDB…";
            StatusText = await service.RefreshAsync(e)
                ? $"Refreshed “{e.Title}” from IGDB. Your own description and artwork were kept."
                : $"IGDB no longer has the matched game for “{e.Title}”. Use Change Match….";
            Refresh(e.Id);
        }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException or IOException)
        {
            await _ui.ShowMessageAsync("Refresh from IGDB", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveMatchAsync()
    {
        if (SelectedItem?.Edition is not { IgdbId: not null } e) return;
        if (!await _ui.ConfirmAsync("Remove Match", $"Forget the IGDB match for “{e.Title}”?\n\n" +
                                                   "IGDB's description and artwork are removed. Your own description and images stay."))
            return;
        MetadataService.RemoveMatch(_library, AppPaths.ArtworkDir, e.Id);
        Refresh(e.Id);
    }

    public bool IsMatchingAll => _matchAllCts is not null;

    [RelayCommand]
    private async Task MatchUnmatchedAsync()
    {
        if (IsMatchingAll) return;
        if (await RequireMetadataAsync() is not { } service) return;
        var editions = _all.Select(i => i.Edition).Where(e => e.IgdbId is null).ToList();
        if (editions.Count == 0)
        {
            StatusText = "Every game is already matched on IGDB.";
            return;
        }
        _matchAllCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsMatchingAll));
        // Reported synchronously: Progress<T> would post updates that could land after the final summary.
        var progress = new InlineProgress(p =>
            StatusText = p.Title.Length > 0 ? $"Matching on IGDB ({p.Done + 1} of {p.Total}): {p.Title}…" : "Finishing…");
        try
        {
            // Network-bound and asynchronous, so it stays on the UI thread: the database connection
            // is never used from two threads, and the window stays responsive between requests.
            var s = await service.MatchAllAsync(editions, progress, _matchAllCts.Token);
            Refresh();
            var text = $"IGDB: {s.Matched} matched";
            if (s.NeedsMatch + s.NotFound > 0) text += $", {s.NeedsMatch + s.NotFound} need a manual match (filter: Needs IGDB match)";
            if (s.Failed > 0) text += $", {s.Failed} failed";
            if (s.Cancelled) text += " · cancelled";
            StatusText = text + ".";
        }
        catch (IgdbException ex)
        {
            Refresh();
            await _ui.ShowMessageAsync("Match Unmatched Games", ex.Message);
        }
        finally
        {
            _matchAllCts.Dispose();
            _matchAllCts = null;
            OnPropertyChanged(nameof(IsMatchingAll));
        }
    }

    [RelayCommand]
    private void CancelMatching() => _matchAllCts?.Cancel();

    private sealed class InlineProgress(Action<(int Done, int Total, string Title)> report) : IProgress<(int Done, int Total, string Title)>
    {
        public void Report((int Done, int Total, string Title) value) => report(value);
    }

    // ---------------------------------------------------------------- user edits
    [RelayCommand]
    private async Task EditDescriptionAsync()
    {
        if (SelectedItem is not { } item) return;
        var text = await _ui.EditTextAsync("Edit Description",
            "Your description replaces IGDB's and is kept when metadata is refreshed. Clear it to use IGDB's again.",
            item.Metadata.DisplaySummary ?? "");
        if (text is null) return;
        // Saving IGDB's own text unchanged doesn't count as an edit.
        UserMetadata.SetSummary(_library, item.Id, text.Trim() == (item.Metadata.Summary ?? "").Trim() ? null : text);
        Refresh(item.Id);
    }

    private async Task SetArtAsync(ArtKind kind)
    {
        if (SelectedItem is not { } item) return;
        var label = kind switch { ArtKind.Cover => "cover", ArtKind.KeyArt => "key art", _ => "logo" };
        var path = await _ui.PickFileAsync($"Choose a {label} image for {item.Title}", null, "Images",
            "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp");
        if (path is null) return;
        try
        {
            UserMetadata.SetArt(_library, AppPaths.ArtworkDir, item.Id, kind, path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await _ui.ShowMessageAsync("Artwork", ex.Message);
            return;
        }
        Refresh(item.Id);
    }

    [RelayCommand] private Task SetCoverAsync() => SetArtAsync(ArtKind.Cover);
    [RelayCommand] private Task SetKeyArtAsync() => SetArtAsync(ArtKind.KeyArt);
    [RelayCommand] private Task SetLogoAsync() => SetArtAsync(ArtKind.Logo);

    [RelayCommand]
    private void ResetArtwork()
    {
        if (SelectedItem is not { } item) return;
        foreach (var kind in Enum.GetValues<ArtKind>())
            UserMetadata.ResetArt(_library, AppPaths.ArtworkDir, item.Id, kind);
        Refresh(item.Id);
        StatusText = $"“{item.Title}” uses IGDB's artwork again.";
    }
}
