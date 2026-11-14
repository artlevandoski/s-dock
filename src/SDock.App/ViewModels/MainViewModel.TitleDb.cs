using CommunityToolkit.Mvvm.Input;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.App.ViewModels;

/// <summary>
/// TitleDB: the eShop's names, descriptions, square icons, banners and screenshots, matched exactly by title ID.
/// It is the first source; IGDB and SteamGridDB fill in what it lacks (logos, and anything it doesn't list).
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Func<TitleDbClient> _titleDbFactory;
    private TitleDbClient? _titleDb;

    private static TitleDbClient DefaultTitleDbFactory() => new(Path.Combine(AppPaths.DataDir, "titledb"));

    private TitleDbClient TitleDb => _titleDb ??= _titleDbFactory();

    public bool HasTitleDbFile => TitleDb.HasFile;

    /// <summary>
    /// Settings → Update from TitleDB: downloads TitleDB if there's a newer copy, then fills in every game it
    /// lists. Images already downloaded are kept, so running it again is quick.
    /// </summary>
    [RelayCommand]
    private async Task UpdateFromTitleDbAsync()
    {
        if (IsMatchingAll) return;
        _matchAllCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsMatchingAll));
        try
        {
            if (!await DownloadTitleDbAsync(_matchAllCts.Token)) return;
            await ApplyTitleDbAsync(_all.Select(i => i.Edition).ToList(), _matchAllCts.Token);
        }
        finally
        {
            _matchAllCts.Dispose();
            _matchAllCts = null;
            OnPropertyChanged(nameof(IsMatchingAll));
        }
    }

    /// <summary>Returns false if there's no usable copy of TitleDB afterwards.</summary>
    private async Task<bool> DownloadTitleDbAsync(CancellationToken ct)
    {
        StatusText = "Checking for a newer TitleDB…";
        var client = TitleDb;
        // The download runs on a worker thread; each progress update hops back to the UI thread.
        var context = SynchronizationContext.Current;
        void Show(TitleDbProgress p) => StatusText = $"Downloading TitleDB ({p.File}, {p.Index + 1} of {p.Count})… " +
            (p.Total is { } total ? $"{p.Done >> 20} of {total >> 20} MB" : $"{p.Done >> 20} MB");
        var progress = new InlineProgress<TitleDbProgress>(p =>
        {
            if (context is null) Show(p);
            else context.Post(_ => Show(p), null);
        });
        try
        {
            await Task.Run(() => client.DownloadAsync(progress, ct), ct);
            if (UseNxVersions)
            {
                StatusText = "Checking nx-versions…";
                await Task.Run(() => NxVersions.DownloadAsync(client, ct), ct); // on failure the saved copy is used
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "TitleDB download cancelled.";
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            if (client.HasFile)
            {
                StatusText = "Couldn't check for a newer TitleDB; using the copy already downloaded.";
                return true;
            }
            await _ui.ShowMessageAsync("TitleDB", "TitleDB couldn't be downloaded:\n\n" + ex.Message +
                                                  "\n\nCheck your internet connection and try again.");
            return false;
        }
    }

    private async Task ApplyTitleDbAsync(IReadOnlyList<Edition> editions, CancellationToken ct)
    {
        var service = new TitleDbService(_library, TitleDb, AppPaths.ArtworkDir);
        var progress = new InlineProgress<(int Done, int Total, string Title)>(p => StatusText = p.Done == 0
            ? $"{p.Title}…"
            : $"TitleDB ({p.Done} of {p.Total}): {p.Title}…");
        TitleDbSummary s;
        try
        {
            var folder = TitleDb.Folder;
            var versions = UseNxVersions ? await Task.Run(() => NxVersions.Load(folder), ct) : null;
            s = await service.ApplyAsync(editions, progress, ct, versions: versions);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            await _ui.ShowMessageAsync("TitleDB", "The downloaded TitleDB file couldn't be read:\n\n" + ex.Message +
                                                  "\n\nUse Update from TitleDB to download it again.");
            return;
        }
        Refresh();
        ScanInstalledContent();
        var text = $"TitleDB: {s.Found} found";
        if (s.NotFound > 0) text += $", {s.NotFound} not listed (filter: Not in TitleDB)";
        if (s.ImagesFailed > 0) text += $", {s.ImagesFailed} image(s) failed (run it again later)";
        if (s.Cancelled) text += " · cancelled";
        StatusText = text + ".";
    }

    /// <summary>After an import: fill in the new games from TitleDB, offering to download it the first time.</summary>
    private async Task ApplyTitleDbToNewGamesAsync(IReadOnlyList<string> added)
    {
        if (added.Count == 0 || IsMatchingAll) return;
        if (!TitleDb.HasFile)
        {
            if (await _ui.ConfirmAsync("TitleDB",
                    "Fill in names, descriptions and pictures from TitleDB (the eShop's own listings)?\n\n" +
                    "Games are matched by their title ID. S-Dock downloads TitleDB's English and Japanese listings " +
                    "once (about 360 MB, merged into one smaller file) and keeps it in its data folder. You can also do this later from Settings → Update from TitleDB."))
                await UpdateFromTitleDbAsync();
            return;
        }
        var editions = added.Select(_library.GetEdition).OfType<Edition>().ToList();
        _matchAllCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsMatchingAll));
        try
        {
            await ApplyTitleDbAsync(editions, _matchAllCts.Token);
        }
        finally
        {
            _matchAllCts.Dispose();
            _matchAllCts = null;
            OnPropertyChanged(nameof(IsMatchingAll));
        }
    }

    /// <summary>
    /// Game Actions → Clear Match and Artwork: start this game over. Everything downloaded or chosen for it is
    /// removed; then it's offered a fresh look-up in TitleDB.
    /// </summary>
    [RelayCommand]
    private async Task ClearMatchAndArtworkAsync()
    {
        if (SelectedItem is not { } item || IsMatchingAll) return;
        var choice = await _ui.ChooseAsync("Clear Match and Artwork", $"Start “{item.Title}” over?",
            "Removes its TitleDB and IGDB matches, descriptions, cover, background and logo, including any you chose " +
            "yourself, and deletes its artwork folder. The game's files, versions, sort title and settings stay.\n\n" +
            "Look it up in TitleDB again afterwards, or leave it bare and match it on IGDB yourself.",
            ClearAndRefetch, ClearOnly, "Cancel");
        if (choice is not (ClearAndRefetch or ClearOnly)) { _ui.RestoreFocus(); return; }
        MetadataService.ClearAll(_library, AppPaths.ArtworkDir, item.Id);
        Refresh(item.Id);
        StatusText = $"Cleared the match and artwork for “{item.Title}”.";
        if (choice == ClearAndRefetch)
            await RefreshFromTitleDbAsync();
        _ui.RestoreFocus();
    }

    private const string ClearAndRefetch = "Clear and Look Up Again", ClearOnly = "Clear Only";

    /// <summary>
    /// Game Actions → Refresh from TitleDB: fetch this game's text, icon and banner again (replacing the downloaded
    /// ones). Your own description and pictures stay.
    /// </summary>
    [RelayCommand]
    private async Task RefreshFromTitleDbAsync()
    {
        if (SelectedItem is not { } item || IsMatchingAll) return;
        if (!TitleDb.HasFile)
        {
            if (await _ui.ConfirmAsync("TitleDB", "TitleDB hasn't been downloaded yet (about 360 MB). Download it and fill in your library now?"))
                await UpdateFromTitleDbAsync();
            return;
        }
        _matchAllCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsMatchingAll));
        try
        {
            StatusText = $"Refreshing “{item.Title}” from TitleDB…";
            var folder = TitleDb.Folder;
            var versions = UseNxVersions ? await Task.Run(() => NxVersions.Load(folder)) : null;
            var s = await new TitleDbService(_library, TitleDb, AppPaths.ArtworkDir)
                .ApplyAsync([item.Edition], null, _matchAllCts.Token, refresh: true, versions: versions);
            Refresh(item.Id);
            StatusText = s.Found == 0
                ? $"“{item.Title}” isn't in TitleDB" + (item.Edition.ConsoleTitleId is { } tid ? $" (title ID {tid})." : " (no title ID in its file name).")
                : $"Refreshed “{item.Title}” from TitleDB." + (s.ImagesFailed > 0 ? " Some pictures couldn't be downloaded; try again later." : "");
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            await _ui.ShowMessageAsync("Refresh from TitleDB", ex.Message);
        }
        finally
        {
            _matchAllCts.Dispose();
            _matchAllCts = null;
            OnPropertyChanged(nameof(IsMatchingAll));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
