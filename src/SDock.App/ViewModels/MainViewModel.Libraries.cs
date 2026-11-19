using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

public sealed partial class LibraryTab(string id) : ObservableObject
{
    public string Id { get; } = id;
    public string Label => Libraries.Label(Id);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header), nameof(Tooltip))]
    public partial int Count { get; set; }

    public string Header => Label;
    public string Tooltip => $"{Label}: {Count} game(s)";
}

/// <summary>A filter dropdown entry. Shared instances so SelectedItem compares by reference.</summary>
public sealed record FilterOption(string Value, string Label)
{
    public const string AllValue = "all";
    public const string HiddenValue = "hidden";
    public const string NotInTitleDbValue = "not_in_titledb";
    public const string UpdateAvailableValue = "update_available";

    public static readonly IReadOnlyList<FilterOption> Generation =
    [
        new(AllValue, "All Games"),
        new(Generations.Switch1, "Switch 1"),
        new(Generations.Switch2, "Switch 2"),
        new(Generations.Unclassified, "Unclassified"),
        new(UpdateAvailableValue, "Update available"),
        new(NotInTitleDbValue, "Not in TitleDB"),
        new(HiddenValue, "Hidden games"),
    ];

    /// <summary>Shown as pills: All Games, Switch 1, Switch 2.</summary>
    public static readonly IReadOnlyList<FilterOption> Primary = Generation.Take(3).ToList();

    /// <summary>In the "More" dropdown: Unclassified, Update available, Not in TitleDB, Hidden games.</summary>
    public static readonly IReadOnlyList<FilterOption> More = Generation.Skip(3).ToList();

    public static readonly IReadOnlyList<FilterOption> SeriesWithoutNeogeo =
    [
        new(AllValue, "All series"),
        new(ArcadeSeries.Aca, ArcadeSeries.Label(ArcadeSeries.Aca)),
        new(ArcadeSeries.Aca2, ArcadeSeries.Label(ArcadeSeries.Aca2)),
    ];

    public static readonly FilterOption NeogeoSeries = new(ArcadeSeries.AcaNeogeo, ArcadeSeries.Label(ArcadeSeries.AcaNeogeo));
    public static readonly FilterOption SegaAgesSeries = new(ArcadeSeries.SegaAges, ArcadeSeries.Label(ArcadeSeries.SegaAges));

    public static readonly IReadOnlyList<FilterOption> SeriesWithNeogeo = [.. SeriesWithoutNeogeo, NeogeoSeries];

    /// <summary>The series dropdown for the opted-in extras (shared instances, so selection compares by reference).</summary>
    public static IReadOnlyList<FilterOption> Series(bool neogeo, bool segaAges)
    {
        var list = SeriesWithoutNeogeo.ToList();
        if (neogeo) list.Add(NeogeoSeries);
        if (segaAges) list.Add(SegaAgesSeries);
        return list;
    }

    public override string ToString() => Label;
}

/// <summary>Details-panel "Library" dropdown entry. Id null = automatic placement by title.</summary>
public sealed record LibraryChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Library tabs, per-library search/filters/view/selection, and the Grid/Carousel toggle.</summary>
public sealed partial class MainViewModel
{
    public const string ViewGrid = "grid";
    public const string ViewCarousel = "carousel";

    private List<EditionItem> _all = [];
    private bool _applyingFilter;
    private bool _loadingState;

    /// <summary>Search text is kept per library for the session (not saved).</summary>
    private readonly Dictionary<string, string> _searchByLibrary = new() { [Libraries.Main] = "", [Libraries.Arcade] = "", [Libraries.Favorites] = "", [Libraries.Recent] = "" };

    public ObservableCollection<LibraryTab> LibraryTabs { get; } =
        [new(Libraries.Main), new(Libraries.Arcade), new(Libraries.Favorites), new(Libraries.Recent)];
    public bool IsFavoritesTab => CurrentLibrary == Libraries.Favorites;
    public bool IsRecentTab => CurrentLibrary == Libraries.Recent;

    /// <summary>Whether a game shows under a tab: its own library, Favorites when it's a favorite, Recently Played once played.</summary>
    private static bool InTab(EditionItem item, string tab) => tab switch
    {
        Libraries.Favorites => item.IsFavorite,
        Libraries.Recent => item.Play is not null,
        _ => item.Library == tab,
    };
    public IReadOnlyList<FilterOption> GenerationFilterOptions => FilterOption.Generation;

    public static string StateKey(string library, string name) => $"lib:{library}:{name}";

    private void InitLibraries()
    {
        _loadingState = true;
        var saved = _library.GetSetting("current_library");
        SelectedLibraryTab = LibraryTabs.FirstOrDefault(t => t.Id == saved) ?? LibraryTabs[0];
        _includeNeogeo = _library.IncludeAcaNeogeo;
        _includeSegaAges = _library.IncludeSegaAges;
        // S-Dock opens in the launch view (Settings → Library). Earlier builds saved the last view, some per library;
        // that value is the launch view until it's changed.
        IsCarousel = LaunchInCarousel;
        LoadLibraryState();
        _loadingState = false;
    }

    /// <summary>The view S-Dock opens in (grid or carousel), shared by all libraries.</summary>
    public const string ViewKey = "view";

    /// <summary>Settings → Library → Launch view. Y (or the view buttons) only switch the view for now.</summary>
    public bool LaunchInCarousel
    {
        get => (_library.GetSetting(ViewKey) ?? _library.GetSetting(StateKey(Libraries.Main, "view"))) == ViewCarousel;
        set
        {
            _library.SetSetting(ViewKey, value ? ViewCarousel : ViewGrid);
            OnPropertyChanged();
        }
    }

    // ----------------------------------------------------------------- library tabs
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLibrary), nameof(IsArcade), nameof(IsFavoritesTab), nameof(IsRecentTab))]
    public partial LibraryTab SelectedLibraryTab { get; set; }

    public string CurrentLibrary => SelectedLibraryTab?.Id ?? Libraries.Main;
    public bool IsArcade => CurrentLibrary == Libraries.Arcade;

    partial void OnSelectedLibraryTabChanging(LibraryTab value)
    {
        // Remember the outgoing library's search before switching.
        if (SelectedLibraryTab is { } old)
            _searchByLibrary[old.Id] = SearchText;
    }

    partial void OnSelectedLibraryTabChanged(LibraryTab value)
    {
        if (value is null || _loadingState) return;
        _library.SetSetting("current_library", value.Id);
        _loadingState = true;
        LoadLibraryState();
        _loadingState = false;
        ApplyFilter(_library.GetSetting(StateKey(value.Id, "selected")));
    }

    /// <summary>Restore this library's saved filters and session search (the view is shared).</summary>
    private void LoadLibraryState()
    {
        var lib = CurrentLibrary;
        GenerationFilter = FilterOption.Generation.FirstOrDefault(o => o.Value == _library.GetSetting(StateKey(lib, "generation")))
                           ?? FilterOption.Generation[0];
        SeriesFilter = SeriesFilterOptions.FirstOrDefault(o => o.Value == _library.GetSetting(StateKey(lib, "series")))
                       ?? SeriesFilterOptions[0];
        SearchText = _searchByLibrary.GetValueOrDefault(lib, "");
    }

    [RelayCommand]
    private void NextLibrary() => SelectedLibraryTab = LibraryTabs[(LibraryTabs.IndexOf(SelectedLibraryTab) + 1) % LibraryTabs.Count];

    [RelayCommand]
    private void PreviousLibrary() =>
        SelectedLibraryTab = LibraryTabs[(LibraryTabs.IndexOf(SelectedLibraryTab) - 1 + LibraryTabs.Count) % LibraryTabs.Count];

    // ----------------------------------------------------------------- view
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGrid), nameof(ViewToggleLabel), nameof(BackdropOpacity))]
    public partial bool IsCarousel { get; set; }

    public bool IsGrid => !IsCarousel;

    /// <summary>The selected game's art fills the window in the carousel and stays faint behind the grid.</summary>
    public double BackdropOpacity => IsCarousel ? 0.8 : 0.16;
    public string ViewToggleLabel => IsCarousel ? "Grid View" : "Carousel View";

    [RelayCommand]
    private void ToggleView() => IsCarousel = !IsCarousel;

    [RelayCommand]
    private void ShowGrid() => IsCarousel = false;

    [RelayCommand]
    private void ShowCarousel() => IsCarousel = true;

    /// <summary>No games at all (as opposed to none matching the filters): offer Add Folder.</summary>
    public bool IsLibraryEmpty => _all.Count == 0;

    // ----------------------------------------------------------------- search and filters
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    partial void OnSearchTextChanged(string value)
    {
        _searchByLibrary[CurrentLibrary] = value ?? "";
        if (!_loadingState) ApplyFilter();
    }

    [ObservableProperty]
    public partial FilterOption GenerationFilter { get; set; } = FilterOption.Generation[0];

    partial void OnGenerationFilterChanged(FilterOption value)
    {
        if (value is null) { GenerationFilter = FilterOption.Generation[0]; return; } // ComboBox teardown
        OnPropertyChanged(nameof(PillFilter));
        OnPropertyChanged(nameof(MoreFilter));
        if (_loadingState) return;
        _library.SetSetting(StateKey(CurrentLibrary, "generation"), value.Value);
        ApplyFilter();
    }

    public IReadOnlyList<FilterOption> PrimaryFilters => FilterOption.Primary;
    public IReadOnlyList<FilterOption> MoreFilters => FilterOption.More;

    /// <summary>The selected pill, or null when a "More" filter is active. Nulls pushed back by the list are ignored.</summary>
    public FilterOption? PillFilter
    {
        get => FilterOption.Primary.Contains(GenerationFilter) ? GenerationFilter : null;
        set { if (value is not null) GenerationFilter = value; }
    }

    /// <summary>The active "More" filter, or null when a pill is selected.</summary>
    public FilterOption? MoreFilter
    {
        get => FilterOption.More.Contains(GenerationFilter) ? GenerationFilter : null;
        set { if (value is not null) GenerationFilter = value; }
    }

    public IReadOnlyList<FilterOption> SeriesFilterOptions => FilterOption.Series(IncludeAcaNeogeo, IncludeSegaAges);

    [ObservableProperty]
    public partial FilterOption SeriesFilter { get; set; } = FilterOption.SeriesWithoutNeogeo[0];

    partial void OnSeriesFilterChanged(FilterOption value)
    {
        if (value is null) { SeriesFilter = SeriesFilterOptions[0]; return; }
        if (_loadingState) return;
        _library.SetSetting(StateKey(CurrentLibrary, "series"), value.Value);
        ApplyFilter();
    }

    private bool _includeNeogeo;

    /// <summary>Optional: show ACA NEOGEO titles in Arcade Archives (otherwise they stay in Main Library).</summary>
    public bool IncludeAcaNeogeo
    {
        get => _includeNeogeo;
        set
        {
            if (!SetProperty(ref _includeNeogeo, value)) return;
            _library.IncludeAcaNeogeo = value;
            SeriesOptionsChanged();
        }
    }

    private bool _includeSegaAges;

    /// <summary>Optional: show SEGA AGES titles in Arcade Archives (otherwise they stay in Main Library).</summary>
    public bool IncludeSegaAges
    {
        get => _includeSegaAges;
        set
        {
            if (!SetProperty(ref _includeSegaAges, value)) return;
            _library.IncludeSegaAges = value;
            SeriesOptionsChanged();
        }
    }

    private void SeriesOptionsChanged()
    {
        OnPropertyChanged(nameof(SeriesFilterOptions));
        var keep = SeriesFilterOptions.FirstOrDefault(o => o.Value == SeriesFilter?.Value) ?? SeriesFilterOptions[0];
        _loadingState = true;
        SeriesFilter = keep;
        _loadingState = false;
        Refresh();
    }

    public bool HasActiveFilters =>
        SearchText.Length > 0 || GenerationFilter.Value != FilterOption.AllValue || (IsArcade && SeriesFilter.Value != FilterOption.AllValue);

    [RelayCommand]
    private void ClearFilters()
    {
        _loadingState = true;
        SearchText = "";
        GenerationFilter = FilterOption.Generation[0];
        SeriesFilter = SeriesFilterOptions[0];
        _library.SetSetting(StateKey(CurrentLibrary, "generation"), FilterOption.AllValue);
        _library.SetSetting(StateKey(CurrentLibrary, "series"), FilterOption.AllValue);
        _loadingState = false;
        ApplyFilter();
    }

    private bool Matches(EditionItem item)
    {
        if (!InTab(item, CurrentLibrary)) return false;
        var q = SearchText.Trim();
        if (q.Length > 0 && !item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
        var g = GenerationFilter?.Value ?? FilterOption.AllValue;
        if (item.IsHidden != (g == FilterOption.HiddenValue)) return false; // hidden games show only under Hidden games
        var keep = g switch
        {
            FilterOption.AllValue => true,
            FilterOption.HiddenValue => true,
            FilterOption.NotInTitleDbValue => !item.Metadata.HasTitleDb,
            FilterOption.UpdateAvailableValue => HasUpdateAvailable(item),
            _ => item.Generation == g,
        };
        if (!keep) return false;
        if (IsArcade && (SeriesFilter?.Value ?? FilterOption.AllValue) is var s && s != FilterOption.AllValue && item.Series != s) return false;
        return true;
    }

    /// <summary>Rebuild the visible covers for the current library and restore the selection.</summary>
    private void ApplyFilter(string? selectId = null)
    {
        selectId ??= SelectedItem?.Id ?? _library.GetSetting(StateKey(CurrentLibrary, "selected"))
                     ?? (CurrentLibrary == Libraries.Main ? _library.GetSetting(LastSelectedKey) : null);
        _applyingFilter = true;
        var shown = _all.Where(Matches);
        if (IsRecentTab) shown = shown.OrderByDescending(i => i.Play!.LastPlayedUtc); // most recently played first
        Items = new ObservableCollection<EditionItem>(shown); // one update instead of one per game
        _applyingFilter = false;
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
        WarmCovers();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }

    public bool ShowEmpty => !HasItems;

    public string EmptyText =>
        _all.Count == 0
            ? "Your library is empty.\nAdd a folder of your game files to get started,\nthen set up your emulator from the menu."
            : GenerationFilter?.Value == FilterOption.HiddenValue
                ? "No hidden games in this library.\nUse Game Actions → Hide Game to tidy away games you don't play."
            : _all.Any(i => InTab(i, CurrentLibrary) && !i.IsHidden)
                ? "No games match the search or filters."
                : IsRecentTab
                    ? "Nothing played yet.\nGames you play show up here, most recent first."
                : IsFavoritesTab
                    ? "No favorites yet.\nMark a game with the ☆ button beside Play, or Game Actions → Add to Favorites."
                : IsArcade
                    ? "No Arcade Archives titles yet.\nTitles starting with “Arcade Archives” or “Arcade Archives 2” appear here automatically."
                    : "No games in Main Library.";

    // ----------------------------------------------------------------- details: library placement
    public ObservableCollection<LibraryChoice> LibraryChoices { get; } = [];

    [ObservableProperty]
    public partial LibraryChoice? SelectedLibraryChoice { get; set; }

    private bool _rebuildingLibraryChoices;

    private void RebuildLibraryChoices()
    {
        _rebuildingLibraryChoices = true;
        LibraryChoices.Clear();
        if (SelectedItem is { } item)
        {
            var automatic = ArcadeSeries.LibraryFor(item.Title, null, IncludeAcaNeogeo, IncludeSegaAges);
            LibraryChoices.Add(new LibraryChoice(null, $"Automatic ({Libraries.Label(automatic)})"));
            LibraryChoices.Add(new LibraryChoice(Libraries.Main, Libraries.Label(Libraries.Main)));
            LibraryChoices.Add(new LibraryChoice(Libraries.Arcade, Libraries.Label(Libraries.Arcade)));
            var pinned = _library.GetLibraryOverride(item.Id);
            SelectedLibraryChoice = LibraryChoices.FirstOrDefault(c => c.Id == pinned) ?? LibraryChoices[0];
        }
        else
        {
            SelectedLibraryChoice = null;
        }
        _rebuildingLibraryChoices = false;
    }

    partial void OnSelectedLibraryChoiceChanged(LibraryChoice? value)
    {
        if (_rebuildingLibraryChoices || SelectedItem is not { } item) return;
        if (value is null) { RebuildLibraryChoices(); return; } // ComboBox teardown
        if (value.Id == _library.GetLibraryOverride(item.Id)) return;
        _library.SetLibraryOverride(item.Id, value.Id);
        Refresh(item.Id);
        if (_all.FirstOrDefault(i => i.Id == item.Id) is { } moved && moved.Library != CurrentLibrary)
        {
            Select(item.Id); // follow the game to its new library
            StatusText = $"Moved “{item.Title}” to {Libraries.Label(moved.Library)}.";
        }
    }
}
