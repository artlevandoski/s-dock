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

    // ------------------------------------------------------------ SteamGridDB
    private readonly Func<LibraryStore, SteamGridDbClient?> _sgdbFactory;
    private SteamGridDbClient? _sgdb;
    private string? _sgdbKey;

    private static SteamGridDbClient? DefaultSgdbFactory(LibraryStore library) =>
        library.HasSteamGridDbKey ? new SteamGridDbClient(library.SteamGridDbKey) : null;

    private SteamGridDbClient? Sgdb
    {
        get
        {
            var key = _library.SteamGridDbKey;
            if (_sgdb is null || key != _sgdbKey)
            {
                _sgdb?.Dispose();
                _sgdb = _sgdbFactory(_library);
                _sgdbKey = key;
            }
            return _sgdb;
        }
    }

    private ArtworkSources Artwork => new(_library, Igdb, Sgdb, AppPaths.ArtworkDir, TitleDb);

    /// <summary>
    /// Fetches logos for the given games: IGDB's first, SteamGridDB's as a fallback. Quietly skipped
    /// when neither IGDB credentials nor a SteamGridDB key are set.
    /// </summary>
    public sealed record LogoSummary(int Added, int NotFound, int Failed, bool Cancelled, string? Error = null);

    /// <summary>
    /// Fetches logos (IGDB first, then SteamGridDB) for matched games that have none, with progress in the
    /// status bar. Quietly does nothing when neither IGDB credentials nor a SteamGridDB key are set.
    /// </summary>
    private async Task<LogoSummary> FetchLogosAsync(IReadOnlyList<Edition> editions, bool replace, CancellationToken ct)
    {
        var sources = Artwork;
        if (!sources.CanFetchLogos) return new LogoSummary(0, 0, 0, false);
        int added = 0, none = 0, failed = 0;
        for (var i = 0; i < editions.Count; i++)
        {
            if (ct.IsCancellationRequested) return new LogoSummary(added, none, failed, true);
            var e = editions[i];
            StatusText = $"Getting logos ({i + 1} of {editions.Count}): {e.Title}…";
            try
            {
                if (await sources.FetchLogoAsync(e, replace, ct)) added++;
                else none++;
            }
            catch (OperationCanceledException)
            {
                return new LogoSummary(added, none, failed, true);
            }
            catch (IgdbException ex) when (ex.Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return new LogoSummary(added, none, failed, false, ex.Message); // bad key: stop rather than fail every game
            }
            catch (Exception ex) when (ex is IgdbException or HttpRequestException or IOException)
            {
                failed++; // one game failing shouldn't stop the rest
            }
        }
        return new LogoSummary(added, none, failed, false);
    }

    /// <summary>Matched games with no logo yet (neither downloaded nor your own).</summary>
    private List<Edition> GamesMissingLogos() => _all
        .Where(i => i.IsMatched && i.Metadata.LogoFile is null && i.Metadata.UserLogoFile is null)
        .Select(i => i.Edition).ToList();

    [RelayCommand]
    private async Task GetMissingLogosAsync()
    {
        if (IsMatchingAll) return;
        if (!Artwork.CanFetchLogos)
        {
            if (await _ui.ConfirmAsync("Logos", "Logos come from IGDB (and optionally SteamGridDB), which need your own " +
                                                "free credentials. Open Metadata Settings now?"))
                await IgdbSettingsAsync();
            if (!Artwork.CanFetchLogos) return;
        }
        var todo = GamesMissingLogos();
        if (todo.Count == 0)
        {
            StatusText = "Every matched game already has a logo.";
            return;
        }
        _matchAllCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsMatchingAll));
        try
        {
            var r = await FetchLogosAsync(todo, replace: false, _matchAllCts.Token);
            Refresh();
            var text = $"Logos: {r.Added} added";
            if (r.NotFound > 0) text += $", {r.NotFound} not on IGDB" + (Sgdb is null ? " (a SteamGridDB key in Metadata Settings may find more)" : " or SteamGridDB");
            if (r.Failed > 0) text += $", {r.Failed} failed (try again later)";
            if (r.Cancelled) text += " · cancelled";
            StatusText = text + ".";
            if (r.Error is not null)
                await _ui.ShowMessageAsync("Get Missing Logos", r.Error);
        }
        finally
        {
            _matchAllCts.Dispose();
            _matchAllCts = null;
            OnPropertyChanged(nameof(IsMatchingAll));
        }
    }

    // ------------------------------------------------------------ choosers
    private async Task ChooseArtAsync(ArtKind kind)
    {
        if (SelectedItem is not { } item) return;
        var sources = Artwork;
        var edition = item.Edition;
        var vm = new ArtPickerViewModel(kind, item.Title, sources.HasSteamGridDb, _ui, ct => kind == ArtKind.Logo
            ? sources.LogoCandidatesAsync(edition, ct)
            : sources.BackgroundCandidatesAsync(edition, ct));
        var pick = await _ui.ShowArtPickerAsync(vm);
        if (pick is null) { _ui.RestoreFocus(); return; }
        try
        {
            if (pick.UseDefault)
                UserMetadata.ResetArt(_library, AppPaths.ArtworkDir, item.Id, kind);
            else if (pick.File is { } file)
                UserMetadata.SetArt(_library, AppPaths.ArtworkDir, item.Id, kind, file);
            else if (pick.Candidate is { } candidate)
            {
                StatusText = "Downloading image…";
                await sources.UseAsync(item.Id, kind, candidate);
            }
            Refresh(item.Id);
            StatusText = pick.UseDefault
                ? $"“{item.Title}” uses the default {(kind == ArtKind.Logo ? "logo" : "background")} again."
                : $"{(kind == ArtKind.Logo ? "Logo" : "Background")} updated for “{item.Title}”.";
        }
        catch (Exception ex) when (ex is IgdbException or HttpRequestException or IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            await _ui.ShowMessageAsync(kind == ArtKind.Logo ? "Choose Logo" : "Choose Background", ex.Message);
        }
        _ui.RestoreFocus();
    }

    [RelayCommand] private Task ChooseBackgroundAsync() => ChooseArtAsync(ArtKind.KeyArt);
    [RelayCommand] private Task ChooseLogoAsync() => ChooseArtAsync(ArtKind.Logo);

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
    public Bitmap? SelectedKeyArt => FromCache(ImageCache.Backdrops, SelectedItem?.Metadata.DisplayKeyArtFile, nameof(SelectedKeyArt));
    public Bitmap? SelectedLogo => FromCache(ImageCache.Logos, SelectedItem?.Metadata.DisplayLogoFile, nameof(SelectedLogo));
    public bool HasLogo => SelectedLogo is not null;

    /// <summary>The image if it's decoded; otherwise null now, and a notification once it's ready (if still selected).</summary>
    private Bitmap? FromCache(ImageCache cache, string? file, string property)
    {
        if (file is null) return null;
        var path = ArtPath(file);
        if (cache.TryGet(path) is { } ready) return ready;
        var forItem = SelectedItem;
        cache.Request(path, _ =>
        {
            if (SelectedItem != forItem) return;
            OnPropertyChanged(property);
            if (property == nameof(SelectedLogo)) OnPropertyChanged(nameof(HasLogo));
        });
        return null;
    }

    /// <summary>
    /// Covers for the games around the selection go first; then every cover in the library (including the
    /// other library) is decoded in the background, so scrolling and switching never wait for artwork.
    /// </summary>
    private void WarmCovers()
    {
        var index = SelectedItem is { } s ? Items.IndexOf(s) : 0;
        var nearby = Enumerable.Range(Math.Max(0, index - 24), 49)
            .Where(i => i < Items.Count).Select(i => Items[i].CoverPath).OfType<string>().ToList();
        ImageCache.Covers.Prefetch(_all.Select(i => i.CoverPath).OfType<string>());
        ImageCache.Covers.Prioritize(nearby);
    }

    /// <summary>Preloads backgrounds and logos for the games either side of the selection (carousel browsing).</summary>
    private void PrefetchNeighbours()
    {
        if (SelectedItem is not { } s) return;
        var index = Items.IndexOf(s);
        if (index < 0) return;
        var around = Enumerable.Range(index - 3, 7).Where(i => i >= 0 && i < Items.Count).Select(i => Items[i].Metadata).ToList();
        ImageCache.Backdrops.Prefetch(around.Select(m => m.DisplayKeyArtFile).OfType<string>().Select(ArtPath));
        ImageCache.Logos.Prefetch(around.Select(m => m.DisplayLogoFile).OfType<string>().Select(ArtPath));
        ImageCache.Covers.Prioritize(Enumerable.Range(index - 12, 25).Where(i => i >= 0 && i < Items.Count)
            .Select(i => Items[i].CoverPath).OfType<string>());
    }
    public string SelectedSummary => SelectedItem?.Metadata.DisplaySummary ?? "";
    public bool HasSummary => SelectedSummary.Length > 0;

    /// <summary>"Platformer · Adventure · 2023".</summary>
    public string SelectedFactsLine
    {
        get
        {
            if (SelectedItem?.Metadata is not { } m) return "";
            var parts = m.DisplayGenres.Take(3).ToList();
            if (m.DisplayReleaseYear is { } y) parts.Add(y.ToString());
            if (m.TdbPublisher is { } publisher) parts.Add(publisher);
            if (m.TdbPlayers is { } players) parts.Add(players == 1 ? "1 player" : $"Up to {players} players");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Carousel tags after the generation: series and up to two genres.</summary>
    public IReadOnlyList<string> SelectedTags
    {
        get
        {
            if (SelectedItem is not { } item) return [];
            var tags = new List<string>();
            if (item.Series != ArcadeSeries.None) tags.Add(ArcadeSeries.Label(item.Series));
            tags.AddRange(item.Metadata.DisplayGenres.Take(2));
            if (item.VersionCount > 1) tags.Add($"{item.VersionCount} versions");
            if (HasUpdateAvailable(item)) tags.Add("Update available");
            return tags;
        }
    }

    public string SelectedGenerationLabel => SelectedItem is { } i ? Generations.Label(i.Generation) : "";

    /// <summary>Details panel: "Switch 1 · Platform · Adventure · 2023".</summary>
    public string DetailsFactsLine
    {
        get
        {
            if (SelectedItem is not { } item) return "";
            var parts = new List<string> { Generations.Label(item.Generation) };
            if (item.Series != ArcadeSeries.None) parts.Add(ArcadeSeries.Label(item.Series));
            parts.AddRange(item.Metadata.DisplayGenres.Take(2));
            if (item.Metadata.DisplayReleaseYear is { } y) parts.Add(y.ToString());
            return string.Join("  ·  ", parts);
        }
    }

    public string MatchStatusText => SelectedItem switch
    {
        null => "",
        { Metadata.TdbName: { } tdb, IsMatched: true, Metadata.IgdbName: { } name } => $"TitleDB: {tdb}  ·  IGDB: {name}",
        { Metadata.TdbName: { } tdb } => $"TitleDB: {tdb}",
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
        var vm = new IgdbSettingsViewModel(id, secret, _ui, (i, s) => new IgdbClient(i, s))
        {
            SteamGridDbKey = _library.SteamGridDbKey,
            SteamGridDbFactory = key => new SteamGridDbClient(key),
        };
        if (await _ui.ShowIgdbSettingsAsync(vm))
        {
            var hadKey = _library.HasSteamGridDbKey;
            _library.IgdbCredentials = (vm.ClientId, vm.ClientSecret);
            _library.SteamGridDbKey = vm.SteamGridDbKey;
            StatusText = "Metadata settings saved.";
            if (!hadKey && _library.HasSteamGridDbKey && _all.Any(i => i.IsMatched))
                StatusText += " Use Settings → Get Missing Logos to look for more logos for your matched games.";
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
            var logos = await FetchLogosAsync([_library.GetEdition(e.Id)!], replace: false, CancellationToken.None);
            Refresh(e.Id);
            StatusText = $"Matched “{e.Title}” to {game.Name} on IGDB." + (logos.Added > 0 ? " Logo added." : "");
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
            var refreshed = await service.RefreshAsync(e);
            if (refreshed)
                await FetchLogosAsync([_library.GetEdition(e.Id)!], replace: true, CancellationToken.None);
            StatusText = refreshed
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
            var sources = Artwork;
            var logos = 0;
            var s = await service.MatchAllAsync(editions, progress, _matchAllCts.Token, async (id, ct) =>
            {
                // Each game's logo right after its match, so stopping early keeps everything fetched so far.
                if (sources.CanFetchLogos && _library.GetEdition(id) is { } matched && await sources.FetchLogoAsync(matched, ct: ct))
                    logos++;
            });
            Refresh();
            var text = $"IGDB: {s.Matched} matched";
            if (logos > 0) text += $", {logos} logo(s)";
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
            "Your description replaces the downloaded one (TitleDB or IGDB) and is kept when metadata is refreshed. Clear it to use the downloaded one again.",
            item.Metadata.DisplaySummary ?? "");
        if (text is null) return;
        // Saving IGDB's own text unchanged doesn't count as an edit.
        UserMetadata.SetSummary(_library, item.Id, text.Trim() == (item.Metadata.DefaultSummary ?? "").Trim() ? null : text);
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
