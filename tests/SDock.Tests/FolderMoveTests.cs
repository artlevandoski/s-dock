using Avalonia.Headless.XUnit;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Move Game Folder: games now somewhere else keep everything, in one step.</summary>
public sealed class FolderMoveTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private string Old => Path.Combine(_env.Root, "Old PC", "Switch");
    private string New => Path.Combine(_env.Root, "New Drive", "Games", "Switch");

    private static string Put(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
        return path;
    }

    /// <summary>A library with three games from the old folder (now gone), and the games in the new folder.</summary>
    private (LibraryStore Lib, string Alpha, string Bravo, string Charlie) Setup()
    {
        var lib = _env.OpenLibrary();
        var folder = lib.UpsertFolder(Old, Generations.Switch1);
        var alpha = lib.AddEdition("Alpha", Generations.Switch1, Put(Path.Combine(Old, "Alpha [0100AAAA00000000].nsp")), folderId: folder);
        var bravo = lib.AddEdition("Bravo", Generations.Switch1, Put(Path.Combine(Old, "Sub", "Bravo.xci")), folderId: folder);
        var charlie = lib.AddEdition("Charlie", Generations.Switch1, Put(Path.Combine(Old, "Charlie.nsp")), folderId: folder);
        lib.SetFavorite(alpha, true);
        lib.SetSortTitle(bravo, "Zzz Bravo");
        Directory.Delete(Path.Combine(_env.Root, "Old PC"), recursive: true); // the old PC's folder isn't here

        Put(Path.Combine(New, "Alpha [0100AAAA00000000].nsp"));              // same place
        Put(Path.Combine(New, "Moved Around", "Bravo.xci"));                  // somewhere else in the folder
        // Charlie didn't come along.
        return (lib, alpha, bravo, charlie);
    }

    [Fact]
    public void Planned_by_relative_path_then_by_name_and_applied_keeping_settings()
    {
        var (lib, alpha, bravo, charlie) = Setup();
        using var _ = lib;
        Assert.Equal(3, Assert.Single(lib.GameFolders()).Missing);

        var plan = lib.PlanFolderMove(Old, New);
        Assert.Equal(2, plan.Games);
        Assert.Equal("Charlie.nsp", Path.GetFileName(Assert.Single(plan.NotFound).Path));
        Assert.Equal(0, plan.Duplicates);
        Assert.False(lib.GetEdition(alpha)!.PrimaryFile!.Exists);             // planning changes nothing

        lib.ApplyFolderMove(plan);
        Assert.Equal(Path.Combine(New, "Alpha [0100AAAA00000000].nsp"), lib.GetEdition(alpha)!.PrimaryFile!.Path);
        Assert.Equal(Path.Combine(New, "Moved Around", "Bravo.xci"), lib.GetEdition(bravo)!.PrimaryFile!.Path);
        Assert.True(lib.GetEdition(charlie)!.IsMissing);                       // still missing, still there
        Assert.Contains(alpha, lib.FavoriteEditions());                        // settings kept
        Assert.Equal("Zzz Bravo", lib.GetEdition(bravo)!.SortTitle);
        var folder = Assert.Single(lib.GameFolders());                         // the folder record moved too
        Assert.Equal(New, folder.Path);
        Assert.Equal(1, folder.Missing);
        Assert.Equal(Generations.Switch1, lib.FolderGeneration(New));
    }

    [Fact]
    public void Entries_added_again_from_the_new_place_merge_into_the_originals()
    {
        var (lib, alpha, _, _) = Setup();
        using var _lib = lib;
        // On the new PC, Add Folder was used before the move: Alpha came in a second time.
        var newFolder = lib.UpsertFolder(New, Generations.Switch1);
        var duplicate = lib.AddEdition("Alpha", Generations.Switch1, Path.Combine(New, "Alpha [0100AAAA00000000].nsp"), folderId: newFolder);
        Assert.Equal(4, lib.ListEditions().Count);

        var plan = lib.PlanFolderMove(Old, New);
        Assert.Equal(1, plan.Duplicates);
        lib.ApplyFolderMove(plan);

        Assert.Null(lib.GetEdition(duplicate));                                // the extra entry is gone
        Assert.Equal(3, lib.ListEditions().Count);
        Assert.True(lib.GetEdition(alpha)!.PrimaryFile!.Exists);               // the original has the file
        Assert.Contains(alpha, lib.FavoriteEditions());
        Assert.Equal(New, Assert.Single(lib.GameFolders()).Path);              // one folder record, the new one
    }

    [Fact]
    public void A_name_found_twice_isnt_guessed()
    {
        var (lib, _, bravo, _) = Setup();
        using var _lib = lib;
        Put(Path.Combine(New, "Other Copy", "Bravo.xci"));
        var plan = lib.PlanFolderMove(Old, New);
        Assert.Contains(plan.NotFound, f => f.EditionId == bravo);
    }

    [AvaloniaFact]
    public async Task From_settings_it_asks_where_shows_what_will_change_and_moves()
    {
        var (lib, alpha, _, _) = Setup();
        using var _lib = lib;
        var ui = new FakeUi { NextPickedFolder = New, ChooseButton = "Move" };
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        Assert.Equal("3 games missing", vm.MissingGamesText);

        await vm.MoveGameFolderCommand.ExecuteAsync(null);
        var question = ui.Messages.Single(m => m.StartsWith("Move 2 games"));
        Assert.Contains("1 file(s) weren't found", question);
        Assert.Equal("1 game missing", vm.MissingGamesText);
        Assert.False(vm.Items.Single(i => i.Id == alpha).IsMissing);
        Assert.Contains("Done: 2 games", ui.Messages.Last());
    }

    [AvaloniaFact]
    public async Task Cancelled_or_nothing_found_changes_nothing()
    {
        var (lib, alpha, _, _) = Setup();
        using var _lib = lib;
        var ui = new FakeUi { NextPickedFolder = New, ChooseButton = null };  // the question is closed
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        await vm.MoveGameFolderCommand.ExecuteAsync(null);
        Assert.True(lib.GetEdition(alpha)!.IsMissing);

        var empty = Path.Combine(_env.Root, "Empty");
        Directory.CreateDirectory(empty);
        ui.NextPickedFolder = empty;
        await vm.MoveGameFolderCommand.ExecuteAsync(null);
        Assert.Contains("Nothing was changed", ui.Messages.Last());
    }
}
