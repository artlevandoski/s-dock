using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.App.ViewModels;

// PC versions: recompilations and ports that are their own .exe and run without an emulator.
public sealed partial class MainViewModel
{
    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex BracketedTitleIdRe();

    /// <summary>
    /// Settings → Add Game (.exe)…: adds a PC version of a game. It's filed as Switch 2 (change it in the details)
    /// and always runs on its own. A title ID in its file or folder name (e.g. "[0100…]") lets TitleDB fill it in.
    /// </summary>
    [RelayCommand]
    private async Task AddProgramAsync()
    {
        var path = await _ui.PickFileAsync("Choose the game's program (.exe)", null, "Programs", "*.exe");
        if (path is null) { _ui.RestoreFocus(); return; }
        path = Path.GetFullPath(path);
        if (_library.FindEditionByPath(path) is { } existing)
        {
            Select(existing);
            StatusText = "That program is already in your library.";
            _ui.RestoreFocus();
            return;
        }
        var suggested = SuggestProgramTitle(path);
        var name = await _ui.EditTextAsync("Add Game",
            "The name to show for this game. It runs on its own, without an emulator, and is filed as Switch 2 " +
            "(change that in the details panel). If it's a version of a console game, use Game Actions → Game Info and " +
            "Matching → Set Title ID… so TitleDB can fill in its description and pictures.", suggested);
        _ui.RestoreFocus();
        if (name is null) return;
        name = string.Join(" ", name.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (name.Length == 0) name = suggested;

        var titleId = TitleIdIn(path);
        var size = new FileInfo(path).Length;
        var id = _library.AddEdition(name, Generations.Switch2, path, ContentKinds.Pc, titleId, sizeBytes: size);
        Refresh(id);
        Select(id);
        StatusText = $"Added “{name}”. It runs on its own, without an emulator.";
        if (titleId is not null)
            await ApplyTitleDbToNewGamesAsync([id]);
    }

    /// <summary>A title ID written in brackets in the program's file name or one of its folders, if any.</summary>
    public static string? TitleIdIn(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if (BracketedTitleIdRe().Match(Path.GetFileName(p)) is { Success: true } m)
                return m.Groups[1].Value.ToUpperInvariant();
        return null;
    }

    /// <summary>The program's product name if it has a real one, else its folder's (or file's) name, tidied.</summary>
    public static string SuggestProgramTitle(string path)
    {
        try
        {
            var product = FileVersionInfo.GetVersionInfo(path).ProductName?.Trim();
            if (product is { Length: > 1 } && !product.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                !product.Equals("Unity", StringComparison.OrdinalIgnoreCase))
                return product;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        // Folders like "bin" or "x64" say nothing; a folder named after the game does.
        var source = folder.Length > 2 && !Regex.IsMatch(folder, @"^(bin|x64|x86|win64|build|release|game)$", RegexOptions.IgnoreCase)
            ? folder : stem;
        var title = LibraryScanner.CleanTitle(source);
        return title.Length > 0 ? title : stem;
    }

    /// <summary>
    /// Game Actions → Game Info and Matching → Set Title ID…: the console title ID of the game this is a version of
    /// (16 characters, e.g. 0100…), so TitleDB can match it. Clear it to remove.
    /// </summary>
    [RelayCommand]
    private async Task SetTitleIdAsync()
    {
        if (SelectedItem is not { } item) return;
        var text = await _ui.EditTextAsync("Set Title ID",
            "The game's title ID: 16 letters and numbers, starting 0100 (for example 01006D9026E1E000). TitleDB uses it to " +
            "find the game's description and pictures. Clear it to remove the title ID.", item.Edition.ConsoleTitleId ?? "");
        _ui.RestoreFocus();
        if (text is null) return;
        var value = text.Trim().Trim('[', ']').ToUpperInvariant();
        if (value.Length > 0 && (value.Length != 16 || !value.All(Uri.IsHexDigit)))
        {
            await _ui.ShowMessageAsync("Set Title ID", $"“{text.Trim()}” isn't a title ID. It should be 16 letters and numbers, like 01006D9026E1E000.");
            return;
        }
        _library.SetConsoleTitleId(item.Id, value.Length == 0 ? null : value);
        Refresh(item.Id);
        if (value.Length == 0)
        {
            StatusText = $"Removed the title ID from “{item.Title}”.";
            return;
        }
        StatusText = $"Title ID set for “{item.Title}”.";
        if (TitleDb.HasFile) await RefreshFromTitleDbAsync();
    }
}
