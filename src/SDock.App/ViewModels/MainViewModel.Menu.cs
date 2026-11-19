using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

/// <summary>One line in the on-screen menu: an action, a tick (setting), or a page of more entries.</summary>
public sealed partial class MenuEntry : ObservableObject
{
    public required string Label { get; init; }
    public Action? Run { get; init; }
    public Func<IReadOnlyList<MenuEntry>>? Page { get; init; }
    public Func<bool>? Checked { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>Settings (ticks) apply at once and the menu stays open; everything else closes it first.</summary>
    public bool KeepOpen => Checked is not null;

    public bool IsChecked => Checked?.Invoke() ?? false;
    public bool HasCheck => Checked is not null;
    public bool HasPage => Page is not null;
    public double Opacity => Enabled ? 1 : 0.45;

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

// The on-screen menu used for Game Actions (X, the button, or right-click). Settings is a page of its own.
// It is a list inside the window rather than a pop-up, so the controller, keyboard and mouse all work the same way.
public sealed partial class MainViewModel
{
    [ObservableProperty] public partial bool IsMenuOpen { get; set; }
    [ObservableProperty] public partial string MenuTitle { get; set; } = "";
    public ObservableCollection<MenuEntry> MenuItems { get; } = [];
    public bool MenuHasParent => _menuStack.Count > 0;

    /// <summary>Raised when the menu shows a page (the view focuses its first line) or closes (focus goes back to the cover).</summary>
    public event Action? MenuChanged;

    private readonly Stack<(string Title, Func<IReadOnlyList<MenuEntry>> Build)> _menuStack = new();
    private (string Title, Func<IReadOnlyList<MenuEntry>> Build) _menuPage;

    [RelayCommand]
    public void OpenGameMenu()
    {
        if (SelectedItem is not { } item) return;
        _menuStack.Clear();
        ShowPage(item.Title, IsCarousel ? BuildCarouselMenu : BuildGameMenu);
    }

    /// <summary>Opens Game Actions straight at the Emulator page (the carousel's Emulator button).</summary>
    [RelayCommand]
    public void OpenEmulatorMenu()
    {
        if (SelectedItem is not { } item || item.Edition.IsProgram) return;
        _menuStack.Clear();
        ShowPage($"Emulator for {item.Title}", BuildEmulatorPage);
    }

    /// <summary>The carousel keeps its menu short: the emulator, favorites and hiding. The rest is in the grid.</summary>
    private IReadOnlyList<MenuEntry> BuildCarouselMenu()
    {
        var list = new List<MenuEntry>();
        if (SelectedItem is { Edition.IsProgram: false })
            list.Add(Page($"Emulator: {EmulatorChoiceLabel}", BuildEmulatorPage));
        list.Add(Do(FavoriteMenuLabel, ToggleFavoriteCommand));
        list.Add(Do(HideMenuLabel, ToggleHiddenCommand));
        list.Add(Do("More Game Actions (Grid View)", ShowGridCommand));
        return list;
    }

    /// <summary>The game's emulator: the default, a profile, or no emulator. Ticked = the current choice; it applies at once.</summary>
    private IReadOnlyList<MenuEntry> BuildEmulatorPage()
    {
        if (SelectedItem is not { } item) return [];
        var current = _library.GetLaunchOverride(item.Id);
        MenuEntry Choice(string label, string? id) =>
            Tick(label, () => _library.GetLaunchOverride(item.Id) == id, () => SetEmulatorChoice(item, id));
        var list = new List<MenuEntry> { Choice(EmulatorChoices.FirstOrDefault(c => c.Id is null)?.Label ?? "Use default", null) };
        foreach (var c in EmulatorChoices.Where(c => c.Id is not null)) list.Add(Choice(c.Label, c.Id));
        if (current is not null && list.All(e => !e.IsChecked)) list[0] = Choice(list[0].Label, null); // a removed profile
        list.Add(Do("Emulator Profiles…", EmulatorSettingsCommand));
        return list;
    }

    private void ShowPage(string title, Func<IReadOnlyList<MenuEntry>> build)
    {
        _menuPage = (title, build);
        MenuTitle = title;
        MenuItems.Clear();
        foreach (var e in build()) MenuItems.Add(e);
        IsMenuOpen = true;
        OnPropertyChanged(nameof(MenuHasParent));
        OnPropertyChanged(nameof(MenuBackLabel));
        MenuChanged?.Invoke();
    }

    /// <summary>A or Enter or a click on a line.</summary>
    public void ActivateMenu(MenuEntry? entry)
    {
        if (entry is not { Enabled: true }) return;
        if (entry.Page is { } page)
        {
            _menuStack.Push(_menuPage);
            ShowPage(entry.Label, page);
            return;
        }
        if (entry.KeepOpen)
        {
            entry.Run?.Invoke();
            foreach (var e in MenuItems) e.Refresh(); // a radio choice unticks the others
            return;
        }
        CloseMenu();
        entry.Run?.Invoke();
    }

    /// <summary>B or Esc: up one page, or close.</summary>
    public void MenuBack()
    {
        if (_menuStack.Count > 0)
        {
            var (title, build) = _menuStack.Pop();
            ShowPage(title, build);
        }
        else CloseMenu();
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen) return;
        _menuStack.Clear();
        IsMenuOpen = false;
        MenuChanged?.Invoke();
    }

    private static MenuEntry Item(string label, Action run, bool enabled = true) => new() { Label = label, Run = run, Enabled = enabled };
    private static MenuEntry Do(string label, System.Windows.Input.ICommand command, bool enabled = true) =>
        new() { Label = label, Run = () => command.Execute(null), Enabled = enabled };
    private static MenuEntry Tick(string label, Func<bool> isOn, Action toggle) => new() { Label = label, Checked = isOn, Run = toggle };
    private static MenuEntry Page(string label, Func<IReadOnlyList<MenuEntry>> page) => new() { Label = label, Page = page };

    private IReadOnlyList<MenuEntry> BuildGameMenu()
    {
        var list = new List<MenuEntry>();
        if (IsGrid) list.Add(Do("Play", PlayCommand, CanPlay)); // the carousel has Play on screen
        var program = SelectedItem?.Edition.IsProgram ?? false; // a PC version always runs on its own
        if (!program)
        {
            list.Add(Do("Launch With…", LaunchWithCommand, CanPlay));
            list.Add(Do("Play Version…", PlayVersionCommand, CanPlay));
        }
        list.Add(Do("Choose Cover…", ChooseCoverCommand));
        list.Add(Do("Choose Background…", ChooseBackgroundCommand));
        list.Add(Do("Choose Logo…", ChooseLogoCommand));
        list.Add(Do("Rename…", RenameCommand));
        list.Add(Do("Edit Description…", EditDescriptionCommand));
        list.Add(Do("Edit Carousel Text…", EditCarouselTextCommand));
        list.Add(Do("Sort Title…", SetSortTitleCommand));
        list.Add(Do(FavoriteMenuLabel, ToggleFavoriteCommand));
        list.Add(Do(HideMenuLabel, ToggleHiddenCommand));
        if (!program) list.Add(Page($"Emulator: {EmulatorChoiceLabel}", BuildEmulatorPage));
        list.Add(Page("Game Info and Matching", () =>
        [
            Do("Refresh from TitleDB", RefreshFromTitleDbCommand),
            Do("Set Title ID…", SetTitleIdCommand),
            Do(MatchMenuLabel, MatchOnIgdbCommand),
            Do("Refresh from IGDB", RefreshMetadataCommand, IsMatched),
            Do("Remove IGDB Match", RemoveMatchCommand, IsMatched),
            Do("Use Default Artwork", ResetArtworkCommand),
            Do("Clear Match and Artwork…", ClearMatchAndArtworkCommand),
        ]));
        if (IsGrid && !program) list.Add(Do("Add Version File…", AddVersionFileCommand));
        else list.Add(Do("Game Settings (Grid View)", ShowGridCommand));
        list.Add(Do("Remove from Library…", RemoveFromLibraryCommand));
        return list;
    }

    /// <summary>The version being run, e.g. "0.1.0-beta.1" (from the build; without the source commit suffix).</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(MainViewModel).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
            return v.Split('+')[0];
        }
    }

    /// <summary>The view closes the window (with the usual "emulator still running" check).</summary>
    public event Action? QuitRequested;
}

// Jump to the next or previous letter (by sort title): Up/Down in the carousel, LT/RT, Page Up/Down.
public sealed partial class MainViewModel
{
    public string MenuBackLabel => MenuHasParent ? "Back" : "Close";

    [ObservableProperty] public partial string JumpLetter { get; set; } = "";
    [ObservableProperty] public partial bool ShowJumpLetter { get; set; }

    /// <summary>The letter a game is filed under: the first letter of its sort title, or # for numbers and symbols.</summary>
    public static string LetterOf(EditionItem item)
    {
        foreach (var c in item.Edition.SortKey)
        {
            if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
            if (char.IsDigit(c)) return "#";
        }
        return "#";
    }

    /// <summary>
    /// Selects the first game of the next letter (+1) or of the previous letter (-1). Returns the letter, or null
    /// when there's nowhere further to go.
    /// </summary>
    public string? JumpToLetter(int direction)
    {
        if (Items.Count == 0 || IsRecentTab) return null; // Recently Played is in date order, not by letter
        var index = SelectedItem is { } s ? Math.Max(0, Items.IndexOf(s)) : 0;
        var current = LetterOf(Items[index]);
        int? target = null;
        if (direction > 0)
        {
            for (var i = index + 1; i < Items.Count && target is null; i++)
                if (LetterOf(Items[i]) != current) target = i;
        }
        else
        {
            var start = index;
            while (start > 0 && LetterOf(Items[start - 1]) == current) start--;
            if (start > 0)
            {
                var previous = LetterOf(Items[start - 1]);
                var j = start - 1;
                while (j > 0 && LetterOf(Items[j - 1]) == previous) j--;
                target = j;
            }
        }
        if (target is not { } t) return null;
        SelectedItem = Items[t];
        JumpLetter = LetterOf(Items[t]);
        ShowJumpLetter = true;
        return JumpLetter;
    }

    partial void OnIsMenuOpenChanged(bool value) => OnPropertyChanged(nameof(MenuBackLabel));
}
