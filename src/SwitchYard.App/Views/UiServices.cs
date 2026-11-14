using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SwitchYard.App.ViewModels;

namespace SwitchYard.App.Views;

/// <summary>Real dialogs and pickers. Dialogs open over the topmost open window.</summary>
public sealed class UiServices(MainWindow main) : IUiServices
{
    private readonly Stack<Window> _dialogs = new();

    private Window Owner => _dialogs.Count > 0 ? _dialogs.Peek() : main;

    private async Task<T?> ShowDialog<T>(Window dialog)
    {
        var owner = Owner;
        _dialogs.Push(dialog);
        try
        {
            return await dialog.ShowDialog<T?>(owner);
        }
        finally
        {
            _dialogs.Pop();
        }
    }

    private async Task<IStorageFolder?> FolderOrNull(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return null;
        try { return await Owner.StorageProvider.TryGetFolderFromPathAsync(path); }
        catch (Exception) { return null; }
    }

    public async Task<string?> PickFolderAsync(string title, string? startDir = null)
    {
        var result = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderOrNull(startDir),
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string? startDir, string filterName, params string[] patterns)
    {
        var result = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderOrNull(startDir),
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public Task ShowMessageAsync(string title, string message) =>
        ShowDialog<string>(new MessageWindow(title, null, message, ["OK"]));

    public async Task<bool> ConfirmAsync(string title, string message) =>
        await ShowDialog<string>(new MessageWindow(title, null, message, ["Yes", "No"])) == "Yes";

    public Task<string?> ChooseAsync(string title, string heading, string message, params string[] buttons) =>
        ShowDialog<string>(new MessageWindow(title, heading, message, buttons));

    public async Task<bool> ShowImportPreviewAsync(ImportPreviewViewModel vm) =>
        await ShowDialog<bool>(new ImportPreviewWindow(vm));

    public async Task<bool> ShowEmulatorSettingsAsync(EmulatorProfilesViewModel vm) =>
        await ShowDialog<bool>(new EmulatorSettingsWindow(vm));

    public async Task<bool> ShowProfilePickerAsync(ProfilePickerViewModel vm) =>
        await ShowDialog<bool>(new ProfilePickerWindow(vm));

    public Task<string?> ShowVersionsAsync(VersionsViewModel vm) =>
        ShowDialog<string>(new VersionsWindow(vm));

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void RestoreFocus()
    {
        // Defer slightly so Windows has finished tearing down the emulator window.
        DispatcherTimer.RunOnce(() =>
        {
            if (main.WindowState == WindowState.Minimized)
                main.WindowState = WindowState.Normal;
            if (_dialogs.Count > 0)
            {
                _dialogs.Peek().Activate();
                return;
            }
            main.Activate();
            main.FocusSelectedCover();
        }, TimeSpan.FromMilliseconds(150));
    }
}
