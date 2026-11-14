using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

/// <summary>Emulator Settings: one editable profile (Milestone 1) with Test Launch.</summary>
public sealed partial class EmulatorSettingsViewModel : ObservableObject
{
    private readonly EmulatorProfile? _original;
    private readonly EmulatorLauncher _launcher;
    private readonly IUiServices _ui;
    private string _emulatorKind;

    public event Action<bool>? CloseRequested;
    public IReadOnlyList<GenerationOption> GenerationOptions => GenerationOption.Profile;
    public string Intro =>
        "Point Switch Yard at an emulator you already have installed. Arguments are passed to the emulator " +
        $"one per line; {LaunchArgs.GamePlaceholder} is replaced by the game file path.";
    public string PresetTooltip => "Sets the arguments to:  " + string.Join("  ", LaunchArgs.EdenPreset) + "\nVerified on Eden v0.2.1";

    public EmulatorSettingsViewModel(EmulatorProfile? profile, EmulatorLauncher launcher, IUiServices ui, string? sampleGame)
    {
        _original = profile;
        _launcher = launcher;
        _ui = ui;
        _emulatorKind = profile?.Emulator ?? "eden";
        SampleGame = sampleGame;
        Name = profile?.Name ?? "Eden";
        Executable = profile?.Executable ?? "";
        WorkingDir = profile?.WorkingDir ?? "";
        Generation = GenerationOption.ForProfile(profile?.Generation ?? Generations.Switch1);
        ArgsText = string.Join("\n", profile?.Args ?? LaunchArgs.EdenPreset);
        _launcher.Launched += OnLaunched;
        _launcher.Finished += OnFinished;
        _launcher.Failed += OnFailed;
    }

    public void Detach()
    {
        _launcher.Launched -= OnLaunched;
        _launcher.Finished -= OnFinished;
        _launcher.Failed -= OnFailed;
    }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string Executable { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string WorkingDir { get; set; }
    [ObservableProperty] public partial GenerationOption Generation { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string ArgsText { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string? SampleGame { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial bool CanTest { get; set; } = true;

    public string CommandPreview => "Command: " + LaunchArgs.Describe(CurrentProfile(), SampleGame ?? "<game file>");

    public EmulatorProfile CurrentProfile()
    {
        var args = ArgsText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var name = string.IsNullOrWhiteSpace(Name) ? "Emulator" : Name.Trim();
        var exe = Executable.Trim().Trim('"');
        var wd = WorkingDir.Trim().Trim('"');
        return _original is null
            ? EmulatorProfile.New(name, exe, args, wd, Generation.Value, _emulatorKind)
            : _original with { Name = name, Executable = exe, WorkingDir = wd, Generation = Generation.Value, Args = args, Emulator = _emulatorKind };
    }

    [RelayCommand]
    private async Task BrowseExecutableAsync()
    {
        var start = Path.GetDirectoryName(Executable);
        if (string.IsNullOrEmpty(start)) start = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = await _ui.PickFileAsync("Choose emulator executable", start, "Programs", "*.exe");
        if (path is not null) Executable = path;
    }

    [RelayCommand]
    private async Task BrowseWorkingDirAsync()
    {
        var start = string.IsNullOrEmpty(WorkingDir) ? Path.GetDirectoryName(Executable) : WorkingDir;
        var path = await _ui.PickFolderAsync("Choose working directory", start);
        if (path is not null) WorkingDir = path;
    }

    [RelayCommand]
    private void UseEdenPreset()
    {
        ArgsText = string.Join("\n", LaunchArgs.EdenPreset);
        _emulatorKind = "eden";
        Generation = GenerationOption.ForProfile(Generations.Switch1);
    }

    [RelayCommand]
    private async Task TestLaunchAsync()
    {
        if (SampleGame is null || !File.Exists(SampleGame))
        {
            var game = await _ui.PickFileAsync("Choose a game file to test with", null, GameFileFilter.Name, GameFileFilter.Patterns);
            if (game is null) return;
            SampleGame = game;
        }
        var profile = CurrentProfile();
        var problems = LaunchArgs.Validate(profile, SampleGame);
        if (problems.Count > 0)
        {
            SetStatus(string.Join("\n", problems), error: true);
            return;
        }
        SetStatus("Starting emulator…");
        _launcher.Launch(profile, SampleGame, MainViewModel.TestLaunchTag);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var p = CurrentProfile();
        if (p.Executable.Length == 0)
        {
            await _ui.ShowMessageAsync("Emulator Settings", "Choose the emulator executable first.");
            return;
        }
        if (!File.Exists(p.Executable) &&
            !await _ui.ConfirmAsync("Emulator Settings", $"The executable doesn't exist:\n{p.Executable}\n\nSave anyway?"))
            return;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

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
        SetStatus($"Emulator {how}. If the game booted straight into gameplay, these settings work. Click Save to keep them.");
    }

    private void OnFailed(string tag, string message)
    {
        if (tag != MainViewModel.TestLaunchTag) return;
        CanTest = true;
        SetStatus(message, error: true);
    }
}
