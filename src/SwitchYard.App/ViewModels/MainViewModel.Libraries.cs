using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

public sealed partial class LibraryTab(string id) : ObservableObject
{
    public string Id { get; } = id;
    public string Label => Libraries.Label(Id);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial int Count { get; set; }

    public string Header => $"{Label}  {Count}";
}

/// <summary>A filter dropdown entry. Shared instances so SelectedItem compares by reference.</summary>
public sealed record FilterOption(string Value, string Label)
{
    public const string AllValue = "all";
    public const string MissingValue = "missing";
    public const string NeedsMatchValue = "needs_match";

    public static readonly IReadOnlyList<FilterOption> Generation =
    [
        new(AllValue, "All generations"),
        new(Generations.Switch1, "Switch 1"),
        new(Generations.Switch2, "Switch 2"),
        new(Generations.Unclassified, "Unclassified"),
        new(MissingValue, "Missing files"),
        new(NeedsMatchValue, "Needs IGDB match"),
    ];

    public static readonly IReadOnlyList<FilterOption> SeriesWithoutNeogeo =
    [
        new(AllValue, "All series"),
        new(ArcadeSeries.Aca, ArcadeSeries.Label(ArcadeSeries.Aca)),
        new(ArcadeSeries.Aca2, ArcadeSeries.Label(ArcadeSeries.Aca2)),
    ];

    public static readonly IReadOnlyList<FilterOption> SeriesWithNeogeo =
        [.. SeriesWithoutNeogeo, new(ArcadeSeries.AcaNeogeo, ArcadeSeries.Label(ArcadeSeries.AcaNeogeo))];

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
    private readonly Dictionary<string, string> _searchByLibrary = new() { [Libraries.Main] = "", [Libraries.Arcade] = "" };

    public ObservableCollection<LibraryTab> LibraryTabs { get; } = [new(Libraries.Main), new(Libraries.Arcade)];
    public IReadOnlyList<FilterOption> GenerationFilterOptions => FilterOption.Generation;

    public static string StateKey(string library, string name) => $"lib:{library}:{name}";

    private void InitLibraries()
    {
        _loadingState = true;
        var saved = _library.GetSetting("current_library");
        SelectedLibraryTab = LibraryTabs.FirstOrDefault(t => t.Id == saved) ?? LibraryTabs[0];
        _includeNeogeo = _library.IncludeAcaNeogeo;
        LoadLibraryState();
        _loadingState = false;
    }

    // ----------------------------------------------------------------- library tabs
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLibrary), nameof(IsArcade))]
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

    /// <summary>Restore this library's saved view, filters and session search.</summary>
    private void LoadLibraryState()
    {
        var lib = CurrentLibrary;
        IsCarousel = _library.GetSetting(StateKey(lib, "view")) == ViewCarousel;
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
    [NotifyPropertyChangedFor(nameof(IsGrid), nameof(ViewToggleLabel))]
    public partial bool IsCarousel { get; set; }

    public bool IsGrid => !IsCarousel;
    public string ViewToggleLabel => IsCarousel ? "Grid View" : "Carousel View";

    partial void OnIsCarouselChanged(bool value)
    {
        if (!_loadingState)
            _library.SetSetting(StateKey(CurrentLibrary, "view"), value ? ViewCarousel : ViewGrid);
    }

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
        if (_loadingState) return;
        _library.SetSetting(StateKey(CurrentLibrary, "generation"), value.Value);
        ApplyFilter();
    }

    public IReadOnlyList<FilterOption> SeriesFilterOptions =>
        IncludeAcaNeogeo ? FilterOption.SeriesWithNeogeo : FilterOption.SeriesWithoutNeogeo;

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
            OnPropertyChanged(nameof(SeriesFilterOptions));
            var keep = SeriesFilterOptions.FirstOrDefault(o => o.Value == SeriesFilter?.Value) ?? SeriesFilterOptions[0];
            _loadingState = true;
            SeriesFilter = keep;
            _loadingState = false;
            Refresh();
        }
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
        if (item.Library != CurrentLibrary) return false;
        var q = SearchText.Trim();
        if (q.Length > 0 && !item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
        var g = GenerationFilter?.Value ?? FilterOption.AllValue;
        var keep = g switch
        {
            FilterOption.AllValue => true,
            FilterOption.MissingValue => item.IsMissing,
            FilterOption.NeedsMatchValue => !item.IsMatched,
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
        Items.Clear();
        foreach (var item in _all.Where(Matches))
            Items.Add(item);
        _applyingFilter = false;
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
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
            : _all.Any(i => i.Library == CurrentLibrary)
                ? "No games match the search or filters."
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
            var automatic = ArcadeSeries.LibraryFor(item.Title, null, IncludeAcaNeogeo);
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
