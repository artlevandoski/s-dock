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
    public void ExitGame()
    {
        if (!Launcher.IsRunning) return;
        StatusText = "Closing the emulator…";
        Launcher.Close(TimeSpan.FromSeconds(5));
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
