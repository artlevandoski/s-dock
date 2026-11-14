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
    Task<bool> ShowEmulatorSettingsAsync(EmulatorSettingsViewModel vm);
    void OpenFolder(string path);

    /// <summary>Bring Switch Yard back to the front with focus on the selected cover (or the open dialog).</summary>
    void RestoreFocus();
}

public static class GameFileFilter
{
    public const string Name = "Switch games";
    public static readonly string[] Patterns = ["*.nsp", "*.xci", "*.nsz", "*.xcz"];
}
