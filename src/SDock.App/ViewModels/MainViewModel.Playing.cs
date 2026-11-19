using SDock.Core;

namespace SDock.App.ViewModels;

// Settings for playing: full screen at launch and how Esc / Start+Select leave a game.
public partial class MainViewModel
{
    private const string FullScreenKey = "play:fullscreen";
    private const string ExitPressesKey = "play:exit_presses";

    /// <summary>Start Eden games in full screen (adds Eden's "-f" switch at launch). On by default.</summary>
    public bool StartGamesFullScreen
    {
        get => _library.GetSetting(FullScreenKey) != "0";
        set
        {
            if (value == StartGamesFullScreen) return;
            _library.SetSetting(FullScreenKey, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Esc / Start+Select must be pressed twice (within a few seconds) to leave a game. On by default, so a
    /// stray press can't end a game by accident.
    /// </summary>
    public bool ExitWithTwoPresses
    {
        get => _library.GetSetting(ExitPressesKey) != "1";
        set
        {
            if (value == ExitWithTwoPresses) return;
            _library.SetSetting(ExitPressesKey, value ? "2" : "1");
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExitWithOnePress));
        }
    }

    public bool ExitWithOnePress
    {
        get => !ExitWithTwoPresses;
        set => ExitWithTwoPresses = !value;
    }

    // The Playing menu's items run these (Avalonia's check and radio menu items only draw the tick).
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleStartGamesFullScreen() => StartGamesFullScreen = !StartGamesFullScreen;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetExitPresses(string? presses) => ExitWithTwoPresses = presses != "1";

    /// <summary>
    /// Closes the running game's emulator the way its close button would (so it shuts down cleanly), stopping it
    /// if it hasn't exited within a few seconds. S-Dock comes back to the front when it has.
    /// </summary>
    /// <summary>When the last exit press (Esc or Start + Select) came.</summary>
    public DateTime? LastExitPressUtc { get; private set; }

    /// <summary>For this long after an exit press, close requests from outside S-Dock are ignored (see MainWindow).</summary>
    public static readonly TimeSpan IgnoreCloseAfterExitPress = TimeSpan.FromSeconds(4);

    /// <summary>
    /// True right after an exit press. Programs such as Steam Input often send Alt+F4 for the same buttons; by the
    /// time it arrives S-Dock may be back in front, and it must not close S-Dock as well as the game.
    /// </summary>
    public bool JustPressedExit => LastExitPressUtc is { } at && DateTime.UtcNow - at < IgnoreCloseAfterExitPress;

    public void NoteExitPress() => LastExitPressUtc = DateTime.UtcNow;

    public void ExitGame()
    {
        if (!Launcher.IsRunning) return;
        StatusText = "Closing the emulator…";
        EventLog.Write("Closing the emulator (exit press)");
        // Asked politely first; if it hasn't closed in a moment (Eden may ask "stop emulation?"), it's stopped.
        Launcher.Close(TimeSpan.FromSeconds(2.5));
    }

    private EmulatorProfile ForLaunch(EmulatorProfile profile) =>
        StartGamesFullScreen ? LaunchArgs.WithFullScreen(profile) : profile;
}

// Hide Game: tidy away games you don't play without removing them.
public partial class MainViewModel
{
    public string HideMenuLabel => SelectedItem is { IsHidden: true } ? "Unhide Game" : "Hide Game";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleHidden()
    {
        if (SelectedItem is not { } item) return;
        var hide = !item.IsHidden;
        // Move to the neighbouring cover so the selection doesn't jump back to the start of the row.
        var index = Items.IndexOf(item);
        var next = Items.Count > 1 ? Items[index + 1 < Items.Count ? index + 1 : index - 1].Id : null;
        _library.SetHidden(item.Id, hide);
        Refresh(next);
        StatusText = hide
            ? $"Hid “{item.Title}”. To bring it back, choose More → Hidden games, then Game Actions → Unhide Game."
            : $"“{item.Title}” is back in {Libraries.Label(item.Library)}.";
    }
}

// Sort title: put a game where you want it in the list (e.g. series in order) without renaming it.
public partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task SetSortTitleAsync()
    {
        if (SelectedItem is not { } item) return;
        var text = await _ui.EditTextAsync("Sort Title",
            $"Games are sorted by this instead of “{item.Title}”, for example “Zelda 1” to keep a series in order. " +
            "Clear it to sort by the title again.",
            item.Edition.SortTitle ?? item.Title);
        _ui.RestoreFocus();
        if (text is null) return;
        var value = string.Join(" ", text.Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries)).Trim();
        _library.SetSortTitle(item.Id, value == item.Title ? null : value);
        Refresh(item.Id);
        StatusText = value.Length == 0 || value == item.Title
            ? $"“{item.Title}” is sorted by its title."
            : $"“{item.Title}” is now sorted as “{value}”.";
    }
}

// Favorites: mark games in any library; they also show in the Favorites tab.
public partial class MainViewModel
{
    public bool SelectedIsFavorite => SelectedItem?.IsFavorite ?? false;
    public string FavoriteMenuLabel => SelectedIsFavorite ? "Remove from Favorites" : "Add to Favorites";
    public string FavoriteGlyph => SelectedIsFavorite ? "★" : "☆";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedItem is not { } item) return;
        var favorite = !item.IsFavorite;
        var keep = item.Id;
        if (!favorite && IsFavoritesTab)
        {
            // Unmarked in Favorites: it leaves the tab, so move to the neighbouring cover.
            var index = Items.IndexOf(item);
            keep = Items.Count > 1 ? Items[index + 1 < Items.Count ? index + 1 : index - 1].Id : null;
        }
        _library.SetFavorite(item.Id, favorite);
        Refresh(keep);
        StatusText = favorite ? $"Added “{item.Title}” to Favorites." : $"Removed “{item.Title}” from Favorites.";
    }
}

// Remove from Library: take a game out of S-Dock with its artwork and settings. The game file is never touched.
public partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        if (SelectedItem is not { } item) return;
        if (IsRunning)
        {
            await _ui.ShowMessageAsync("Remove from Library", "Close the running game first.");
            return;
        }
        var where = item.Edition.PrimaryFile?.Path is { } path ? $"\n\n{path}" : "";
        var choice = await _ui.ChooseAsync("Remove from Library", $"Remove “{item.Title}” from S-Dock?",
            "Its artwork, description, favorite and settings are deleted from S-Dock. " +
            $"The game file itself isn't touched and stays on your PC:{where}\n\n" +
            "Adding its folder again would bring it back. To keep it out of sight but in S-Dock, use Hide Game instead.",
            "Remove", "Cancel");
        _ui.RestoreFocus();
        if (choice != "Remove") return;

        // Move to the neighbouring cover so the selection doesn't jump back to the start of the row.
        var index = Items.IndexOf(item);
        var next = Items.Count > 1 ? Items[index + 1 < Items.Count ? index + 1 : index - 1].Id : null;
        _library.DeleteEdition(item.Id);
        var art = Path.Combine(AppPaths.ArtworkDir, item.Id);
        try { if (Directory.Exists(art)) Directory.Delete(art, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // a picture in use; harmless leftover
        Refresh(next);
        StatusText = $"Removed “{item.Title}” from the library.";
    }
}

// Play history: when each game was last played and for how long in total (Recently Played, "Played 12 h").
public partial class MainViewModel
{
    public const string StartOnKey = "start_on";
    private System.Diagnostics.Stopwatch? _sessionStarted;

    /// <summary>"Played 12 h · last played Tuesday", or "" if never played.</summary>
    public string PlayHistoryText => SelectedItem?.Play is { } p ? p.Describe(DateTime.Now) : "";
    public bool HasPlayHistory => PlayHistoryText.Length > 0;

    /// <summary>Settings → Library → Start on: the last game played (default) or the last one selected.</summary>
    public bool StartOnLastPlayedGame
    {
        get => _library.GetSetting(StartOnKey) != "selected";
        set
        {
            _library.SetSetting(StartOnKey, value ? "played" : "selected");
            OnPropertyChanged();
        }
    }

    public string PlayedCountText
    {
        get
        {
            var n = _all.Count(i => i.Play is not null);
            return n switch { 0 => "Nothing played yet", 1 => "1 game played", _ => $"{n} games played" };
        }
    }

    /// <summary>When S-Dock opens: select the game played last (in its own library), if that's the setting.</summary>
    private void StartOnLastPlayed()
    {
        if (!StartOnLastPlayedGame) return;
        var last = _all.Where(i => i.Play is not null && !i.IsHidden).MaxBy(i => i.Play!.LastPlayedUtc);
        if (last is null) return;
        if (!IsRecentTab && !(IsFavoritesTab && last.IsFavorite) && last.Library != CurrentLibrary)
            SelectedLibraryTab = LibraryTabs.First(t => t.Id == last.Library);
        Select(last.Id);
    }

    /// <summary>A game closed: add the session to its play history (sessions under 30 seconds don't count).</summary>
    private void RecordSession(string tag)
    {
        var length = _sessionStarted?.Elapsed ?? TimeSpan.Zero;
        _sessionStarted = null;
        RecordSession(tag, length);
    }

    /// <summary>Adds a session of this length to a game's history (also used by the tests).</summary>
    public void RecordSession(string editionId, TimeSpan length)
    {
        if (editionId == TestLaunchTag || _all.All(i => i.Id != editionId)) return;
        if (_library.RecordPlay(editionId, length, DateTime.UtcNow))
            Refresh(SelectedItem?.Id ?? editionId);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ClearPlayHistoryAsync()
    {
        if (_all.All(i => i.Play is null)) return;
        if (!await _ui.ConfirmAsync("Clear Play History", "Forget when every game was played and for how long? Recently Played will be empty."))
            return;
        _library.ClearPlayHistory();
        Refresh(SelectedItem?.Id);
    }
}

// Rename: the name shown, sorted by, and used to look the game up online (EmuReady, IGDB).
public partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedItem is not { } item) return;
        var text = await _ui.EditTextAsync("Rename",
            "The name S-Dock shows, sorts by, and uses to look the game up online (EmuReady, IGDB). " +
            "If a rating or match isn't found, try the name as the site lists it.",
            item.Title);
        _ui.RestoreFocus();
        if (text is null) return;
        var name = string.Join(" ", text.Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (name.Length == 0 || name == item.Title) return;
        _library.SetTitle(item.Id, name);
        _library.ForgetEmuReady(item.Id); // look it up again under the new name
        Refresh(item.Id);
        StatusText = $"Renamed to “{name}”.";
        if (SelectedItem is { } renamed && renamed.Id == item.Id) ScheduleEmuReady(renamed);
    }
}
