namespace SwitchYard.App.ViewModels;

/// <summary>Everything the view models need from the windowing layer (faked in tests).</summary>
public interface IUiServices
{
    Task<string?> PickFolderAsync(string title, string? startDir = null);
    Task<string?> PickFileAsync(string title, string? startDir, string filterName, params string[] patterns);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows a message with custom buttons; returns the chosen button's label, or null if closed.</summary>
    Task<string?> ChooseAsync(string title, string heading, string message, params string[] buttons);

    Task<bool> ShowImportPreviewAsync(ImportPreviewViewModel vm);
    Task<bool> ShowEmulatorSettingsAsync(EmulatorProfilesViewModel vm);
    Task<bool> ShowProfilePickerAsync(ProfilePickerViewModel vm);

    /// <summary>Play Version…: returns the file to launch, or null if closed without playing.</summary>
    Task<string?> ShowVersionsAsync(VersionsViewModel vm);

    Task<bool> ShowIgdbSettingsAsync(IgdbSettingsViewModel vm);

    /// <summary>Match on IGDB…: returns the chosen game, or null if cancelled.</summary>
    Task<SwitchYard.Core.Metadata.IgdbGame?> ShowMatchAsync(MatchViewModel vm);

    /// <summary>Choose Background… / Choose Logo…: returns the pick, or null if cancelled.</summary>
    Task<ArtPick?> ShowArtPickerAsync(ArtPickerViewModel vm);

    /// <summary>Multi-line text editor. Returns the text, or null if cancelled.</summary>
    Task<string?> EditTextAsync(string title, string explanation, string initial);

    /// <summary>Opens a web page in the user's browser.</summary>
    void OpenUrl(string url);
    void OpenFolder(string path);

    /// <summary>Bring Switch Yard back to the front with focus on the selected cover (or the open dialog).</summary>
    void RestoreFocus();
}

public static class GameFileFilter
{
    public const string Name = "Switch games";
    public static readonly string[] Patterns = ["*.nsp", "*.xci", "*.nsz", "*.xcz"];
}
