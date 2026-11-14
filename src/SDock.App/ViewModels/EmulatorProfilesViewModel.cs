using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

/// <summary>Emulator Settings: named profiles, generation defaults, and Test Launch.</summary>
public sealed partial class EmulatorProfilesViewModel : ObservableObject
{
    public const string AskEachTime = "Ask each time";

    private readonly EmulatorLauncher _launcher;
    private readonly IUiServices _ui;
    private readonly HashSet<string> _removed = [];
    private bool _rebuildingChoices;

    public ObservableCollection<ProfileEditor> Profiles { get; } = [];
    public ObservableCollection<ProfileChoice> Switch1Choices { get; } = [];
    public ObservableCollection<ProfileChoice> Switch2Choices { get; } = [];
    public ObservableCollection<DiscoveredEmulator> Discovered { get; } = [];
    public IReadOnlyList<GenerationOption> GenerationOptions => GenerationOption.Profile;
    public string EmulatorsDir { get; }

    public event Action<bool>? CloseRequested;

    public string Intro =>
        "Set up one profile per emulator build you want to use. Arguments are passed one per line; " +
        $"{LaunchArgs.GamePlaceholder} is replaced by the game file path.";
    public string PresetTooltip => "Sets the arguments to:  " + string.Join("  ", LaunchArgs.EdenPreset) + "\nVerified on Eden v0.2.1";

    public EmulatorProfilesViewModel(IEnumerable<EmulatorProfile> profiles, string? switch1Default, string? switch2Default,
        EmulatorLauncher launcher, IUiServices ui, string? sampleGame, string emulatorsDir)
    {
        _launcher = launcher;
        _ui = ui;
        SampleGame = sampleGame;
        EmulatorsDir = emulatorsDir;
        foreach (var p in profiles)
            AddEditor(ProfileEditor.From(p));
        RebuildChoices(switch1Default, switch2Default);
        Selected = Profiles.FirstOrDefault();
        Rescan();
        _launcher.Launched += OnLaunched;
        _launcher.Finished += OnFinished;
        _launcher.Failed += OnFailed;
    }

    public void Detach()
    {
        _launcher.Launched -= OnLaunched;
        _launcher.Finished -= OnFinished;
        _launcher.Failed -= OnFailed;
        foreach (var p in Profiles) p.PropertyChanged -= OnEditorChanged;
    }

    // ------------------------------------------------------------------ state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CommandPreview))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateProfileCommand), nameof(RemoveProfileCommand))]
    public partial ProfileEditor? Selected { get; set; }

    private ProfileChoice? _switch1Default, _switch2Default;

    // Ignore the null a ComboBox pushes while it is torn down; only a rebuild may clear these.
    public ProfileChoice? Switch1Default
    {
        get => _switch1Default;
        set { if (value is not null || _rebuildingChoices) SetProperty(ref _switch1Default, value); }
    }

    public ProfileChoice? Switch2Default
    {
        get => _switch2Default;
        set { if (value is not null || _rebuildingChoices) SetProperty(ref _switch2Default, value); }
    }

    /// <summary>Settings captured when Save is clicked, before the window (and its bindings) close.</summary>
    private (List<EmulatorProfile> Profiles, string? Switch1, string? Switch2)? _saved;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(AddDiscoveredCommand))] public partial DiscoveredEmulator? SelectedDiscovered { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string? SampleGame { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial bool CanTest { get; set; } = true;

    public bool HasSelection => Selected is not null;
    public bool HasDiscovered => Discovered.Count > 0;
    public string DiscoveredHint => HasDiscovered
        ? $"{Discovered.Count} emulator build(s) found in the Emulators folder."
        : "No emulator builds found in the Emulators folder yet.";

    public string CommandPreview => Selected is null
        ? ""
        : "Command: " + LaunchArgs.Describe(Selected.ToProfile(), SampleGame ?? "<game file>");

    private void AddEditor(ProfileEditor editor)
    {
        editor.PropertyChanged += OnEditorChanged;
        Profiles.Add(editor);
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileEditor.Name) or nameof(ProfileEditor.Generation))
            RebuildChoices(Switch1Default?.Id, Switch2Default?.Id);
        if (sender == Selected)
            OnPropertyChanged(nameof(CommandPreview));
    }

    /// <summary>Default dropdowns list only profiles that support that generation.</summary>
    private void RebuildChoices(string? keep1, string? keep2)
    {
        _rebuildingChoices = true;
        Fill(Switch1Choices, Generations.Switch1);
        Fill(Switch2Choices, Generations.Switch2);
        Switch1Default = Switch1Choices.FirstOrDefault(c => c.Id == keep1) ?? Switch1Choices[0];
        Switch2Default = Switch2Choices.FirstOrDefault(c => c.Id == keep2) ?? Switch2Choices[0];
        _rebuildingChoices = false;

        void Fill(ObservableCollection<ProfileChoice> list, string generation)
        {
            list.Clear();
            list.Add(new ProfileChoice(null, AskEachTime));
            foreach (var p in Profiles.Where(p => p.ToProfile().Supports(generation)))
                list.Add(new ProfileChoice(p.Id, p.DisplayName));
        }
    }

    /// <summary>A newly added profile becomes the default for any generation that has none yet.</summary>
    private void AdoptAsDefaultIfNone(ProfileEditor editor)
    {
        var p = editor.ToProfile();
        string? d1 = Switch1Default?.Id, d2 = Switch2Default?.Id;
        if (d1 is null && p.Supports(Generations.Switch1)) d1 = p.Id;
        if (d2 is null && p.Supports(Generations.Switch2)) d2 = p.Id;
        RebuildChoices(d1, d2);
    }

    // --------------------------------------------------------------- profiles
    [RelayCommand]
    private void AddProfile()
    {
        var editor = ProfileEditor.Create("New profile");
        AddEditor(editor);
        AdoptAsDefaultIfNone(editor);
        Selected = editor;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateProfile()
    {
        if (Selected is null) return;
        var copy = Selected.Duplicate();
        AddEditor(copy);
        RebuildChoices(Switch1Default?.Id, Switch2Default?.Id);
        Selected = copy;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveProfileAsync()
    {
        if (Selected is not { } editor) return;
        if (!await _ui.ConfirmAsync("Remove profile",
                $"Remove “{editor.DisplayName}”?\n\nGames set to use it will go back to their generation's default. " +
                "The emulator itself and its files are not touched."))
            return;
        var index = Profiles.IndexOf(editor);
        editor.PropertyChanged -= OnEditorChanged;
        Profiles.Remove(editor);
        if (!editor.IsNew) _removed.Add(editor.Id);
        RebuildChoices(Switch1Default?.Id, Switch2Default?.Id);
        Selected = Profiles.Count == 0 ? null : Profiles[Math.Min(index, Profiles.Count - 1)];
    }

    [RelayCommand]
    private void Rescan()
    {
        Discovered.Clear();
        foreach (var d in EmulatorDiscovery.Find(EmulatorsDir))
            Discovered.Add(d);
        SelectedDiscovered = Discovered.FirstOrDefault(d => Profiles.All(p => !SamePath(p.Executable, d.Path)));
        OnPropertyChanged(nameof(HasDiscovered));
        OnPropertyChanged(nameof(DiscoveredHint));
    }

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && string.Equals(AppPaths.PathKey(a.Trim().Trim('"')), AppPaths.PathKey(b), StringComparison.Ordinal);

    private bool CanAddDiscovered() => SelectedDiscovered is not null;

    [RelayCommand(CanExecute = nameof(CanAddDiscovered))]
    private void AddDiscovered()
    {
        if (SelectedDiscovered is not { } d) return;
        if (Profiles.FirstOrDefault(p => SamePath(p.Executable, d.Path)) is { } existing)
        {
            Selected = existing;
            SetStatus($"“{existing.DisplayName}” already uses that executable.");
            return;
        }
        var editor = d.LooksLikeEden
            ? ProfileEditor.Create(d.SuggestedName, d.Path, "eden", LaunchArgs.EdenPreset)
            : ProfileEditor.Create(d.SuggestedName, d.Path, "custom", [LaunchArgs.GamePlaceholder]);
        AddEditor(editor);
        AdoptAsDefaultIfNone(editor);
        Selected = editor;
        SetStatus(d.LooksLikeEden
            ? "Added with the Eden preset. Use Test Launch to confirm it boots a game."
            : "Added with the game path as the only argument. Check this emulator's documentation, then use Test Launch.");
    }

    [RelayCommand]
    private void OpenEmulatorsFolder() => _ui.OpenFolder(EmulatorsDir);

    // ------------------------------------------------------- selected profile
    [RelayCommand]
    private async Task BrowseExecutableAsync()
    {
        if (Selected is not { } editor) return;
        var start = Path.GetDirectoryName(editor.Executable);
        if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) start = EmulatorsDir;
        var path = await _ui.PickFileAsync("Choose emulator executable", start, "Programs", "*.exe");
        if (path is not null) editor.Executable = path;
    }

    [RelayCommand]
    private async Task BrowseWorkingDirAsync()
    {
        if (Selected is not { } editor) return;
        var start = string.IsNullOrEmpty(editor.WorkingDir) ? Path.GetDirectoryName(editor.Executable) : editor.WorkingDir;
        var path = await _ui.PickFolderAsync("Choose working directory", start);
        if (path is not null) editor.WorkingDir = path;
    }

    [RelayCommand]
    private void UseEdenPreset()
    {
        if (Selected is not { } editor) return;
        editor.ArgsText = string.Join("\n", LaunchArgs.EdenPreset);
        editor.EmulatorKind = "eden";
        editor.Generation = GenerationOption.ForProfile(Generations.Switch1);
    }

    [RelayCommand]
    private async Task TestLaunchAsync()
    {
        if (Selected is not { } editor) return;
        if (SampleGame is null || !File.Exists(SampleGame))
        {
            var game = await _ui.PickFileAsync("Choose a game file to test with", null, GameFileFilter.Name, GameFileFilter.Patterns);
            if (game is null) return;
            SampleGame = game;
        }
        var profile = editor.ToProfile();
        var problems = LaunchArgs.Validate(profile, SampleGame);
        if (problems.Count > 0)
        {
            SetStatus(string.Join("\n", problems), error: true);
            return;
        }
        SetStatus($"Starting {profile.Name}…");
        _launcher.Launch(profile, SampleGame, MainViewModel.TestLaunchTag);
    }

    // ------------------------------------------------------------------- save
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Profiles.FirstOrDefault(p => p.ToProfile().Executable.Length == 0) is { } blank)
        {
            Selected = blank;
            SetStatus($"“{blank.DisplayName}” has no executable. Choose one or remove the profile.", error: true);
            return;
        }
        var missing = Profiles.Where(p => !File.Exists(p.ToProfile().Executable)).Select(p => p.DisplayName).ToList();
        if (missing.Count > 0 &&
            !await _ui.ConfirmAsync("Emulator Settings",
                "These profiles point at an executable that doesn't exist:\n\n" + string.Join("\n", missing) + "\n\nSave anyway?"))
            return;
        _saved = (Profiles.Select(p => p.ToProfile()).ToList(), Switch1Default?.Id, Switch2Default?.Id);
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Writes the edited profiles and defaults. Removing a profile clears games that used it.</summary>
    public void ApplyTo(LibraryStore library)
    {
        var (profiles, switch1, switch2) = _saved ?? (Profiles.Select(p => p.ToProfile()).ToList(), Switch1Default?.Id, Switch2Default?.Id);
        library.SaveEmulatorSettings(_removed, profiles, switch1, switch2);
    }

    // ---------------------------------------------------------- launcher events
    private void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusIsError = error;
    }

    private void OnLaunched(string tag, int pid)
    {
        if (tag != MainViewModel.TestLaunchTag) return;
        CanTest = false;
        SetStatus($"Emulator started (PID {pid}). Close it when you've confirmed the game boots.");
    }

    private void OnFinished(string tag, int code, bool crashed)
    {
        if (tag != MainViewModel.TestLaunchTag) return;
        CanTest = true;
        var how = crashed ? "crashed" : $"exited with code {code}";
        SetStatus($"Emulator {how}. If the game booted straight into gameplay, this profile works. Click Save to keep your changes.");
    }

    private void OnFailed(string tag, string message)
    {
        if (tag != MainViewModel.TestLaunchTag) return;
        CanTest = true;
        SetStatus(message, error: true);
    }
}
