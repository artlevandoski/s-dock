using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;
using SwitchYard.Core.Metadata;

namespace SwitchYard.App.ViewModels;

public sealed class EditionItem(Edition edition, string library, bool pinnedToLibrary, EditionMetadata? metadata = null)
{
    /// <summary>Covers are decoded at this width (about 2× their on-screen size in the grid).</summary>
    public const int CoverDecodeWidth = 300;

    private Avalonia.Media.Imaging.Bitmap? _cover;
    private bool _coverLoaded;

    public Edition Edition { get; } = edition;
    public EditionMetadata Metadata { get; } = metadata ?? EditionMetadata.Empty(edition.Id);
    public bool IsMatched => Edition.IgdbId is not null;

    /// <summary>The cover to show: the user's image, else IGDB's, else null (generated placeholder).</summary>
    public Avalonia.Media.Imaging.Bitmap? Cover
    {
        get
        {
            if (!_coverLoaded)
            {
                _coverLoaded = true;
                if (Metadata.DisplayCoverFile is { } f)
                    _cover = Controls.ArtCache.Load(Path.Combine(AppPaths.ArtworkDir, f), CoverDecodeWidth);
            }
            return _cover;
        }
    }
    public string Id => Edition.Id;
    public string Title => Edition.Title;
    public string Generation => Edition.Generation;
    public bool IsMissing { get; } = edition.IsMissing;
    public string Library { get; } = library;
    public bool PinnedToLibrary { get; } = pinnedToLibrary;
    public string Series { get; } = ArcadeSeries.Detect(edition.Title);
    public int VersionCount => Edition.Files.Count;
    public string Tooltip => IsMissing ? $"{Title}\nFile missing. Select it and use Locate File…" : Title;
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string LastSelectedKey = "last_selected_edition";
    public const string TestLaunchTag = "test-launch";

    private readonly LibraryStore _library;
    private readonly IUiServices _ui;
    private bool _suppressGenerationWrite;

    public EmulatorLauncher Launcher { get; }
    public ObservableCollection<EditionItem> Items { get; } = [];
    public IReadOnlyList<GenerationOption> GenerationOptions => GenerationOption.Library;

    /// <param name="igdbFactory">Creates the IGDB client from the saved credentials (tests pass a fake).</param>
    public MainViewModel(LibraryStore library, EmulatorLauncher launcher, IUiServices ui,
        Func<LibraryStore, IgdbClient?>? igdbFactory = null)
    {
        _library = library;
        _ui = ui;
        _igdbFactory = igdbFactory ?? DefaultIgdbFactory;
        Launcher = launcher;
        Launcher.Launched += OnLaunched;
        Launcher.Finished += OnFinished;
        Launcher.Failed += OnFailed;
        InitLibraries();
        Refresh();
    }

    // ------------------------------------------------------------- state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanPlay), nameof(SelectedTitle), nameof(SelectedGeneration),
        nameof(SelectedIsMissing), nameof(DetailsText), nameof(ShowMissing), nameof(VersionsText), nameof(HasVersions), nameof(CarouselSubtitle),
        nameof(SelectedCover), nameof(SelectedKeyArt), nameof(SelectedLogo), nameof(HasLogo), nameof(SelectedSummary),
        nameof(HasSummary), nameof(SelectedFactsLine), nameof(MatchStatusText), nameof(MatchMenuLabel), nameof(IsMatched))]
    public partial EditionItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial GenerationOption? SelectedGenerationOption { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay), nameof(PlayLabel))]
    public partial bool IsRunning { get; set; }

    public bool HasItems => Items.Count > 0;
    public bool HasSelection => SelectedItem is not null;
    public bool CanPlay => HasSelection && !IsRunning;
    public string PlayLabel => IsRunning ? "Running…" : "Play";
    public string SelectedTitle => SelectedItem?.Title ?? "No game selected";
    public string? SelectedGeneration => SelectedItem?.Generation;
    public bool SelectedIsMissing => SelectedItem?.IsMissing ?? false;
    public bool ShowMissing => SelectedIsMissing;

    public string DetailsText
    {
        get
        {
            if (SelectedItem is not { } item) return "";
            var e = item.Edition;
            var lines = new List<string> { $"Type: {ContentKinds.Label(e.ContentKind)}" };
            if (e.ConsoleTitleId is not null) lines.Add($"Title ID: {e.ConsoleTitleId}");
            lines.Add($"File: {e.PrimaryFile?.Path ?? "—"}");
            return string.Join("\n", lines);
        }
    }

    public string? SelectedId => SelectedItem?.Id;

    /// <summary>Tag of the most recent launch (an edition ID, or the Test Launch tag).</summary>
    public string? LastLaunchedTag { get; private set; }

    partial void OnSelectedItemChanged(EditionItem? value)
    {
        if (value is not null && !_applyingFilter)
        {
            _library.SetSetting(LastSelectedKey, value.Id);
            _library.SetSetting(StateKey(CurrentLibrary, "selected"), value.Id);
        }
        RebuildLibraryChoices();
        _suppressGenerationWrite = true;
        SelectedGenerationOption = value is null ? null : GenerationOption.ForLibrary(value.Generation);
        _suppressGenerationWrite = false;
        RebuildEmulatorChoices();
    }

    partial void OnSelectedGenerationOptionChanged(GenerationOption? value)
    {
        if (_suppressGenerationWrite || value is null || SelectedItem is not { } item || value.Value == item.Generation)
            return;
        _library.SetGeneration(item.Id, value.Value);
        Refresh(item.Id);
    }

    // --------------------------------------------------- per-game emulator choice
    /// <summary>Details-panel dropdown: "Use default (…)" plus every profile.</summary>
    public ObservableCollection<ProfileChoice> EmulatorChoices { get; } = [];

    [ObservableProperty]
    public partial ProfileChoice? SelectedEmulatorChoice { get; set; }

    [ObservableProperty]
    public partial string RouteText { get; set; } = "";

    private bool _rebuildingEmulatorChoices;

    public void RebuildEmulatorChoices()
    {
        _rebuildingEmulatorChoices = true;
        EmulatorChoices.Clear();
        if (SelectedItem?.Edition is { } e)
        {
            var fallback = LaunchRouter.DefaultFor(_library, e.Generation);
            EmulatorChoices.Add(new ProfileChoice(null, fallback is null ? "Use default (ask each time)" : $"Use default ({fallback.Name})"));
            foreach (var p in _library.ListProfiles())
            {
                var compatible = e.Generation == Generations.Unclassified || p.Supports(e.Generation);
                EmulatorChoices.Add(new ProfileChoice(p.Id, compatible ? p.Name : $"{p.Name} (set up for {Generations.Label(p.Generation)})"));
            }
            var chosen = _library.GetLaunchOverride(e.Id);
            SelectedEmulatorChoice = EmulatorChoices.FirstOrDefault(c => c.Id == chosen) ?? EmulatorChoices[0];
            var route = LaunchRouter.Resolve(_library, e);
            RouteText = route.Source switch
            {
                RouteSource.GameChoice => $"Plays with {route.Profile!.Name} (this game's choice)",
                RouteSource.GenerationDefault => $"Plays with {route.Profile!.Name} ({Generations.Label(e.Generation)} default)",
                _ => _library.ListProfiles().Count == 0 ? "No emulator set up yet" : "Play will ask which emulator to use",
            };
        }
        else
        {
            SelectedEmulatorChoice = null;
            RouteText = "";
        }
        _rebuildingEmulatorChoices = false;
    }

    partial void OnSelectedEmulatorChoiceChanged(ProfileChoice? value)
    {
        if (_rebuildingEmulatorChoices || SelectedItem is not { } item) return;
        if (value is null)
        {
            // The dropdown briefly clears while items change; fall back to "Use default".
            RebuildEmulatorChoices();
            return;
        }
        _library.SetLaunchOverride(item.Id, value.Id);
        RebuildEmulatorChoices();
    }

    /// <summary>Reload from the database, rebuild the grid, and keep or restore the selection.</summary>
    /// <summary>Reload everything from the database, then show the current library with its filters.</summary>
    public void Refresh(string? selectId = null)
    {
        selectId ??= SelectedItem?.Id;
        var overrides = _library.LibraryOverrides();
        var includeNeogeo = _library.IncludeAcaNeogeo;
        var metadata = _library.AllMetadata();
        _all = _library.ListEditions()
            .Select(e => new EditionItem(e, _library.LibraryOf(e, overrides, includeNeogeo), overrides.ContainsKey(e.Id),
                metadata.GetValueOrDefault(e.Id)))
            .ToList();
        foreach (var tab in LibraryTabs)
            tab.Count = _all.Count(i => i.Library == tab.Id);
        ApplyFilter(selectId);
        var missing = _all.Count(i => i.IsMissing);
        StatusText = $"{_all.Count} game(s) in library" + (missing > 0 ? $" · {missing} with missing files" : "");
    }

    /// <summary>
    /// Selects a game, switching to its library (and clearing that library's search and filters if
    /// they hide it). Used to return to the launched cover after the emulator exits.
    /// </summary>
    public bool Select(string editionId)
    {
        if (Items.FirstOrDefault(i => i.Id == editionId) is { } visible)
        {
            SelectedItem = visible;
            return true;
        }
        if (_all.FirstOrDefault(i => i.Id == editionId) is not { } item) return false;
        if (item.Library != CurrentLibrary)
            SelectedLibraryTab = LibraryTabs.First(t => t.Id == item.Library);
        if (Items.All(i => i.Id != editionId))
            ClearFilters();
        ApplyFilter(editionId);
        return SelectedItem?.Id == editionId;
    }

    // ------------------------------------------------------------- import
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _ui.PickFolderAsync("Choose a folder of game files");
        if (folder is not null)
            await ImportFolderAsync(folder);
    }

    public async Task ImportFolderAsync(string folder)
    {
        var folderGen = _library.FolderGeneration(folder) ?? Generations.Unclassified;
        var result = await Task.Run(() => LibraryScanner.ScanFolder(folder, folderGen, _library));
        if (result.Entries.Count == 0 && result.MissingKnown.Count == 0)
        {
            var extra = result.Errors.Count > 0 ? "\n\n" + string.Join("\n", result.Errors) : "";
            await _ui.ShowMessageAsync("Add Folder",
                "No game files (.nsp, .xci, .nsz, .xcz) were found in that folder or its subfolders." + extra);
            return;
        }
        var preview = new ImportPreviewViewModel(result, folderGen);
        if (!await _ui.ShowImportPreviewAsync(preview))
            return;
        var summary = LibraryScanner.ImportEntries(_library, folder, preview.FolderGeneration.Value, result.Entries);
        Refresh(summary.Added.FirstOrDefault());
        if (summary.Added.FirstOrDefault() is { } first)
            Select(first); // show the library the first new game landed in
        StatusText = $"Imported {summary.Added.Count} new game(s)" +
                     (summary.AddedVersions > 0 ? $" and {summary.AddedVersions} version file(s)" : "") +
                     $"; {summary.AlreadyPresent} already in library; {summary.Skipped} left out.";
    }

    [RelayCommand]
    private async Task LocateFileAsync()
    {
        if (SelectedItem?.Edition is not { PrimaryFile: { } file } edition) return;
        var start = Path.GetDirectoryName(file.Path);
        while (!string.IsNullOrEmpty(start) && !Directory.Exists(start))
            start = Path.GetDirectoryName(start);
        var path = await _ui.PickFileAsync($"Locate {edition.Title}", start, GameFileFilter.Name, GameFileFilter.Patterns);
        if (path is null) return;
        try
        {
            _library.RelinkFile(file.Id, path);
        }
        catch (InvalidOperationException ex)
        {
            await _ui.ShowMessageAsync("Locate File", ex.Message);
            return;
        }
        Refresh(edition.Id);
    }

    [RelayCommand]
    private void OpenDataFolder() => _ui.OpenFolder(AppPaths.DataDir);

    public string DataFolderTip =>
        $"Your library database and logs ({(AppPaths.IsPortable ? "portable" : "user profile")}):\n{AppPaths.DataDir}";

    [RelayCommand]
    private void OpenEmulatorsFolder() => _ui.OpenFolder(AppPaths.EmulatorsDir);

    // ------------------------------------------------------------- emulator
    [RelayCommand]
    private async Task EmulatorSettingsAsync()
    {
        var sample = SelectedItem?.Edition.PrimaryFile is { Exists: true } f ? f.Path : null;
        var vm = new EmulatorProfilesViewModel(_library.ListProfiles(),
            _library.GetGenerationDefault(Generations.Switch1), _library.GetGenerationDefault(Generations.Switch2),
            Launcher, _ui, sample, AppPaths.EmulatorsDir);
        try
        {
            if (await _ui.ShowEmulatorSettingsAsync(vm))
            {
                vm.ApplyTo(_library);
                StatusText = "Emulator settings saved.";
                RebuildEmulatorChoices();
            }
        }
        finally
        {
            vm.Detach();
        }
        _ui.RestoreFocus();
    }

    /// <summary>True when there are no profiles; offers to open Emulator Settings.</summary>
    private async Task<bool> NoProfilesAsync()
    {
        if (_library.ListProfiles().Count > 0) return false;
        if (await _ui.ConfirmAsync("Play", "No emulator is set up yet. Open Emulator Settings now?"))
            await EmulatorSettingsAsync();
        return true;
    }

    private async Task<bool> EmulatorBusyAsync()
    {
        if (!Launcher.IsRunning) return false;
        await _ui.ShowMessageAsync("Play", "An emulator is already running. Close it first.");
        return true;
    }

    [RelayCommand]
    private Task PlayAsync() => PlaySelectedAsync();

    /// <summary>Play: the game's choice, then its generation's default, then ask.</summary>
    /// <param name="filePath">A specific version to launch (Play Version…); null for the game's main file.</param>
    public async Task<bool> PlaySelectedAsync(string? filePath = null)
    {
        if (SelectedItem?.Edition is not { } e) return false;
        if (await EmulatorBusyAsync() || await NoProfilesAsync()) return false;

        var route = LaunchRouter.Resolve(_library, e);
        var profile = route.Profile;
        if (profile is null)
        {
            var picker = new ProfilePickerViewModel(e, _library.ListProfiles(), oneTime: false);
            if (!await _ui.ShowProfilePickerAsync(picker) || picker.Selected is not { } pick) return false;
            profile = pick.Profile;
            if (picker.RememberForGame)
                _library.SetLaunchOverride(e.Id, profile.Id);
            if (picker.MakeDefault && picker.CanMakeDefault)
                _library.SetGenerationDefault(e.Generation, profile.Id);
            RebuildEmulatorChoices();
        }
        return await LaunchAsync(e, profile, filePath);
    }

    // ------------------------------------------------------------------ versions
    public bool HasVersions => SelectedItem?.Edition.HasVersions ?? false;

    /// <summary>Line under the carousel title: generation, series and versions.</summary>
    public string CarouselSubtitle
    {
        get
        {
            if (SelectedItem is not { } item) return "";
            var parts = new List<string> { Generations.Label(item.Generation) };
            if (item.Series != ArcadeSeries.None) parts.Add(ArcadeSeries.Label(item.Series));
            if (item.VersionCount > 1) parts.Add($"{item.VersionCount} versions");
            if (item.IsMissing) parts.Add("file missing");
            return string.Join("  ·  ", parts);
        }
    }

    public string VersionsText => SelectedItem?.Edition is { } e && e.HasVersions
        ? $"Versions: {e.Files.Count} files"
        : "Versions: 1 file";

    [RelayCommand]
    private async Task PlayVersionAsync()
    {
        if (SelectedItem?.Edition is not { } e) return;
        var vm = new VersionsViewModel(e, _library, _ui);
        var play = await _ui.ShowVersionsAsync(vm);
        Refresh(e.Id); // the main file or the list may have changed
        if (play is not null)
            await PlaySelectedAsync(play);
    }

    [RelayCommand]
    private async Task AddVersionFileAsync()
    {
        if (SelectedItem?.Edition is not { } e) return;
        var start = Path.GetDirectoryName(e.PrimaryFile?.Path);
        var path = await _ui.PickFileAsync($"Add another version of {e.Title}", start, GameFileFilter.Name, GameFileFilter.Patterns);
        if (path is null) return;
        try
        {
            _library.AddFile(e.Id, path, new FileInfo(path).Length);
        }
        catch (InvalidOperationException ex)
        {
            await _ui.ShowMessageAsync("Add Version File", ex.Message);
            return;
        }
        Refresh(e.Id);
        StatusText = $"Added {Path.GetFileName(path)} as a version of “{e.Title}”. Use Play Version… to launch it.";
    }

    [RelayCommand]
    private Task LaunchWithAsync() => LaunchSelectedWithAsync();

    /// <summary>Launch With…: a one-time choice that saves nothing.</summary>
    public async Task<bool> LaunchSelectedWithAsync()
    {
        if (SelectedItem?.Edition is not { } e) return false;
        if (await EmulatorBusyAsync() || await NoProfilesAsync()) return false;
        var picker = new ProfilePickerViewModel(e, _library.ListProfiles(), oneTime: true);
        if (!await _ui.ShowProfilePickerAsync(picker) || picker.Selected is not { } pick) return false;
        return await LaunchAsync(e, pick.Profile);
    }

    private async Task<bool> LaunchAsync(Edition e, EmulatorProfile profile, string? filePath = null)
    {
        var game = filePath ?? e.PrimaryFile?.Path ?? "";
        var problems = LaunchArgs.Validate(profile, game);
        if (problems.Count > 0)
        {
            await ShowLaunchProblemAsync(e, $"Profile: {profile.Name}\n\n" + string.Join("\n\n", problems));
            return false;
        }
        IsRunning = true;
        if (!Launcher.Launch(profile, game, e.Id))
        {
            IsRunning = false;
            return false;
        }
        return true;
    }

    private async Task ShowLaunchProblemAsync(Edition? e, string message)
    {
        const string settings = "Emulator Settings…", locate = "Locate File…", close = "Close";
        var buttons = e is { IsMissing: true } ? new[] { settings, locate, close } : new[] { settings, close };
        var choice = await _ui.ChooseAsync("Can't launch", $"{e?.Title ?? "Game"} couldn't be launched.", message, buttons);
        if (choice == settings) await EmulatorSettingsAsync();
        else if (choice == locate) await LocateFileAsync();
    }

    // ------------------------------------------------------- launcher events
    private void OnLaunched(string tag, int pid)
    {
        var title = Items.FirstOrDefault(i => i.Id == tag)?.Title ?? "test game";
        StatusText = $"Emulator running (PID {pid}): {title}";
        LastLaunchedTag = tag;
        IsRunning = true;
    }

    private void OnFinished(string tag, int code, bool crashed)
    {
        StatusText = crashed ? $"Emulator crashed (code {code})." : $"Emulator exited (code {code}).";
        AfterEmulator(tag);
    }

    private async void OnFailed(string tag, string message)
    {
        StatusText = "Launch failed.";
        AfterEmulator(tag);
        if (tag != TestLaunchTag && tag.Length > 0)
            await ShowLaunchProblemAsync(Items.FirstOrDefault(i => i.Id == tag)?.Edition, message);
    }

    private void AfterEmulator(string tag)
    {
        if (tag != TestLaunchTag)
            Select(tag);
        IsRunning = false;
        _ui.RestoreFocus();
    }

    /// <summary>Called when the window is closing while an emulator runs. Returns true if it may close.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!Launcher.IsRunning) return true;
        const string stop = "Stop Emulator and Quit";
        var choice = await _ui.ChooseAsync("Switch Yard", "The emulator is still running.",
            "Close the emulator first so your progress is saved, or force it to stop.", stop, "Cancel");
        if (choice != stop) return false;
        Launcher.Stop();
        return true;
    }
}
