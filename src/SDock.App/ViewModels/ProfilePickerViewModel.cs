using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

public sealed record PickerItem(EmulatorProfile Profile, bool IsCompatible, string Detail)
{
    public string Name => Profile.Name;
}

/// <summary>
/// "Which emulator?" Shown by Play when nothing is set for the game (with options to remember),
/// and by Launch With… as a one-time choice.
/// </summary>
public sealed partial class ProfilePickerViewModel : ObservableObject
{
    private readonly Edition _edition;

    public bool OneTime { get; }
    public IReadOnlyList<PickerItem> Items { get; }
    public event Action<bool>? CloseRequested;

    public ProfilePickerViewModel(Edition edition, IEnumerable<EmulatorProfile> profiles, bool oneTime)
    {
        _edition = edition;
        OneTime = oneTime;
        Items = profiles
            .Select(p => new PickerItem(p, p.Supports(edition.Generation) || edition.Generation == Generations.Unclassified,
                $"{Generations.Label(p.Generation)} · {p.Executable}"))
            .OrderByDescending(i => i.IsCompatible)
            .ToList();
        Selected = Items.FirstOrDefault(i => i.IsCompatible) ?? Items.FirstOrDefault();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMakeDefault))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    public partial PickerItem? Selected { get; set; }

    [ObservableProperty] public partial bool RememberForGame { get; set; }
    [ObservableProperty] public partial bool MakeDefault { get; set; }

    public string GenerationLabel => Generations.Label(_edition.Generation);
    public bool IsClassified => _edition.Generation is Generations.Switch1 or Generations.Switch2;
    public bool ShowRemember => !OneTime;
    public bool ShowMakeDefault => !OneTime && IsClassified;
    public bool CanMakeDefault => Selected?.Profile.Supports(_edition.Generation) ?? false;
    public string MakeDefaultLabel => $"Make it the default for all {GenerationLabel} games";

    public string Heading => OneTime ? $"Launch “{_edition.Title}” with…" : $"Which emulator should play “{_edition.Title}”?";

    public string Explanation => OneTime
        ? "This is a one-time choice. Nothing is saved."
        : IsClassified
            ? $"No default emulator is set for {GenerationLabel} games yet."
            : "This game is Unclassified, so no generation default applies to it.";

    partial void OnSelectedChanged(PickerItem? value)
    {
        if (!CanMakeDefault) MakeDefault = false;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => CloseRequested?.Invoke(true);

    private bool CanPlay() => Selected is not null;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
