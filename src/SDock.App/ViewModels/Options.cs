using SDock.Core;

namespace SDock.App.ViewModels;

/// <summary>Shared option instances so ComboBox SelectedItem bindings compare by reference.</summary>
public sealed record GenerationOption(string Value, string Label)
{
    public static readonly IReadOnlyList<GenerationOption> Library =
        Generations.All.Select(g => new GenerationOption(g, Generations.Label(g))).ToList();

    public static readonly IReadOnlyList<GenerationOption> Profile =
        Generations.ProfileOptions.Select(g => new GenerationOption(g, Generations.Label(g))).ToList();

    public static GenerationOption ForLibrary(string value) => Library.FirstOrDefault(o => o.Value == value) ?? Library[^1];
    public static GenerationOption ForProfile(string value) => Profile.FirstOrDefault(o => o.Value == value) ?? Profile[0];

    public override string ToString() => Label;
}
