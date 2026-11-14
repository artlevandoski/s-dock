using CommunityToolkit.Mvvm.ComponentModel;
using SwitchYard.Core;

namespace SwitchYard.App.ViewModels;

/// <summary>One editable emulator profile inside the Emulator Settings window.</summary>
public sealed partial class ProfileEditor : ObservableObject
{
    public string Id { get; }
    public string EmulatorKind { get; set; }
    public bool IsNew { get; }

    private ProfileEditor(string id, bool isNew, string name, string exe, string workingDir, string generation,
        IEnumerable<string> args, string kind)
    {
        Id = id;
        IsNew = isNew;
        Name = name;
        Executable = exe;
        WorkingDir = workingDir;
        Generation = GenerationOption.ForProfile(generation);
        ArgsText = string.Join("\n", args);
        EmulatorKind = kind;
    }

    public static ProfileEditor From(EmulatorProfile p) =>
        new(p.Id, false, p.Name, p.Executable, p.WorkingDir, p.Generation, p.Args, p.Emulator);

    public static ProfileEditor Create(string name, string exe = "", string kind = "eden", IEnumerable<string>? args = null,
        string generation = Generations.Switch1) =>
        new(Guid.NewGuid().ToString(), true, name, exe, "", generation, args ?? LaunchArgs.EdenPreset, kind);

    public ProfileEditor Duplicate() =>
        new(Guid.NewGuid().ToString(), true, Name + " (copy)", Executable, WorkingDir, Generation.Value, Args, EmulatorKind);

    [ObservableProperty][NotifyPropertyChangedFor(nameof(DisplayName))] public partial string Name { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Subtitle))] public partial string Executable { get; set; }
    [ObservableProperty] public partial string WorkingDir { get; set; }
    private GenerationOption _generation = GenerationOption.Profile[0];

    /// <summary>
    /// Ignores null: a ComboBox being torn down (window closing, editor rebinding) pushes null back
    /// through its two-way binding, which used to blank the profile and crash Save.
    /// </summary>
    public GenerationOption Generation
    {
        get => _generation;
        set
        {
            if (value is null) return;
            if (SetProperty(ref _generation, value))
                OnPropertyChanged(nameof(Subtitle));
        }
    }
    [ObservableProperty] public partial string ArgsText { get; set; }

    public List<string> Args => ArgsText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed profile" : Name.Trim();

    public string Subtitle
    {
        get
        {
            var exe = Executable.Trim().Trim('"');
            var file = exe.Length == 0 ? "no executable" : Path.GetFileName(exe) + (File.Exists(exe) ? "" : " (not found)");
            return $"{Generation.Label} · {file}";
        }
    }

    public EmulatorProfile ToProfile() => new()
    {
        Id = Id,
        Name = DisplayName,
        Executable = Executable.Trim().Trim('"'),
        WorkingDir = WorkingDir.Trim().Trim('"'),
        Generation = Generation.Value,
        Args = Args,
        Emulator = EmulatorKind,
    };
}

/// <summary>An entry in a "which profile" dropdown. Id null means "no profile" (ask, or use the default).</summary>
public sealed record ProfileChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}
