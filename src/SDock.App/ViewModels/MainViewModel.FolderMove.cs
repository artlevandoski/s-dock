using CommunityToolkit.Mvvm.Input;
using SDock.Core;

namespace SDock.App.ViewModels;

// Move Game Folder: when games are now somewhere else (another PC, another drive, a reorganised folder), point the
// whole folder at its new place in one go. Every game keeps its favorites, artwork, sort title and emulator choice.
public sealed partial class MainViewModel
{
    /// <summary>For Settings: "12 games missing" when some can't be found.</summary>
    public string MissingGamesText
    {
        get
        {
            var n = _all.Count(i => i.IsMissing && !i.Edition.IsProgram);
            return n switch { 0 => "", 1 => "1 game missing", _ => $"{n} games missing" };
        }
    }

    [RelayCommand]
    private async Task MoveGameFolderAsync()
    {
        // 1. Which folder moved: the folders games were added from, those with missing games first.
        var folders = _library.GameFolders();
        if (folders.Count == 0)
        {
            await _ui.ShowMessageAsync("Move Game Folder", "No game folders have been added yet. Use Add Folder… first.");
            return;
        }
        if (folders.All(f => f.Missing == 0))
        {
            // Nothing is missing, so nothing has moved, as far as this library knows. The usual reason after copying
            // S-Dock to another PC: the copied data folder isn't the one S-Dock is using.
            await _ui.ShowMessageAsync("Move Game Folder",
                "None of the games in this library are missing, so there's nothing to move.\n\n" +
                $"This library is in:\n{AppPaths.DataDir}\n\n" +
                "If you copied your library from another PC and don't see it here, it isn't in that folder. Close S-Dock, " +
                "put the copied data folder there (next to S-Dock.exe, replacing this one), start S-Dock again, then use " +
                "Move Game Folder.");
            return;
        }
        GameFolder? old;
        var label = (GameFolder f) => f.Missing > 0 ? $"{f.Path}  ({f.Missing} missing)" : f.Path;
        // Only folders with missing games can have moved.
        var moved = folders.Where(f => f.Missing > 0).ToList();
        if (moved.Count == 1)
            old = moved[0];
        else
        {
            var shown = moved.Take(6).ToList();
            var pick = await _ui.ChooseAsync("Move Game Folder", "Which game folder has moved?",
                "Choose the folder as S-Dock knew it. Next you'll choose where it is now.",
                [.. shown.Select(label), "Cancel"]);
            old = shown.FirstOrDefault(f => label(f) == pick);
        }
        if (old is null) return;

        // 2. Where it is now.
        var start = Directory.Exists(old.Path) ? old.Path : null;
        var now = await _ui.PickFolderAsync($"Where is “{Path.GetFileName(old.Path)}” now?", start);
        _ui.RestoreFocus();
        if (now is null) return;
        if (AppPaths.PathKey(now) == AppPaths.PathKey(old.Path))
        {
            await _ui.ShowMessageAsync("Move Game Folder",
                $"That's the folder S-Dock already knows ({old.Path}). Choose where its games are now instead.\n\n" +
                $"This library is in:\n{AppPaths.DataDir}");
            return;
        }

        // 3. What would change, and a yes before changing it.
        var plan = _library.PlanFolderMove(old.Path, now);
        if (plan.Moves.Count == 0)
        {
            await _ui.ShowMessageAsync("Move Game Folder",
                $"None of the {old.Files} game files from\n{old.Path}\nwere found in\n{now}.\n\nNothing was changed.");
            return;
        }
        var games = plan.Games == 1 ? "1 game" : $"{plan.Games} games";
        var message = $"From:  {plan.OldFolder}\nTo:      {plan.NewFolder}\n\n{games} found there; they keep their favorites, artwork and settings.";
        if (plan.Duplicates > 0)
            message += $"\n{plan.Duplicates} of them had been added again from the new place; those extra entries are merged back into the originals.";
        if (plan.NotFound.Count > 0)
            message += $"\n{plan.NotFound.Count} file(s) weren't found and stay missing (Locate File… fixes them one at a time).";
        var choice = await _ui.ChooseAsync("Move Game Folder", $"Move {games} to the new folder?", message, "Move", "Cancel");
        if (choice != "Move") return;

        _library.ApplyFolderMove(plan);
        foreach (var duplicate in plan.Moves.Select(m => m.DuplicateEditionId).OfType<string>().Distinct())
        {
            // The merged-away entries' downloaded pictures aren't needed any more.
            var art = Path.Combine(AppPaths.ArtworkDir, duplicate);
            try { if (Directory.Exists(art)) Directory.Delete(art, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Refresh(SelectedItem?.Id);
        ScanInstalledContent();
        StatusText = $"Moved {games} to {plan.NewFolder}.";
        await _ui.ShowMessageAsync("Move Game Folder", $"Done: {games} now point to\n{plan.NewFolder}." +
            (plan.NotFound.Count > 0 ? $"\n\n{plan.NotFound.Count} file(s) are still missing." : ""));
    }
}
