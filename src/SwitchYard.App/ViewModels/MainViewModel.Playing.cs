using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

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

    /// <summary>
    /// Closes the running game's emulator the way its close button would (so it shuts down cleanly), stopping it
    /// if it hasn't exited within a few seconds. Switch Yard comes back to the front when it has.
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
