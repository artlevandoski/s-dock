namespace SDock.Core;

/// <summary>One game file and where it is now.</summary>
public sealed record FileMove(EditionFile File, string NewPath, string? DuplicateEditionId);

/// <summary>
/// What moving a game folder would do: the files found in the new place (and any duplicate entries that a later
/// Add Folder made for them, which are merged back into the originals), and the files that weren't found.
/// </summary>
public sealed record FolderMovePlan(string OldFolder, string NewFolder, IReadOnlyList<FileMove> Moves, IReadOnlyList<EditionFile> NotFound)
{
    public int Games => Moves.Select(m => m.File.EditionId).Distinct().Count();
    public int Duplicates => Moves.Where(m => m.DuplicateEditionId is not null).Select(m => m.DuplicateEditionId).Distinct().Count();
}

/// <summary>A library folder and how many of its games' files are missing.</summary>
public sealed record GameFolder(string Path, int Files, int Missing);

public sealed partial class LibraryStore
{
    /// <summary>The folders games were added from, with how many of their files can't be found (most missing first).</summary>
    public List<GameFolder> GameFolders()
    {
        var folders = new List<(string Path, string Key)>();
        using (var cmd = Cmd("SELECT path, path_key FROM library_folders"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) folders.Add((r.GetString(0), r.GetString(1)));
        var files = new List<string>();
        using (var cmd = Cmd("SELECT path FROM edition_files"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) files.Add(r.GetString(0));
        return folders
            .Select(f =>
            {
                var inside = files.Where(p => AppPaths.PathKey(p).StartsWith(f.Key + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
                return new GameFolder(f.Path, inside.Count, inside.Count(p => !File.Exists(p)));
            })
            .Where(f => f.Files > 0)
            .OrderByDescending(f => f.Missing).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Works out where each file under <paramref name="oldFolder"/> is inside <paramref name="newFolder"/>: at the same
    /// place relative to the folder, or else the only file of that name anywhere under it. Changes nothing.
    /// </summary>
    public FolderMovePlan PlanFolderMove(string oldFolder, string newFolder)
    {
        oldFolder = Path.GetFullPath(oldFolder).TrimEnd(Path.DirectorySeparatorChar);
        newFolder = Path.GetFullPath(newFolder).TrimEnd(Path.DirectorySeparatorChar);
        var files = FilesUnder(oldFolder);
        Dictionary<string, List<string>>? byName = null; // built only if something isn't at its relative place
        var moves = new List<FileMove>();
        var notFound = new List<EditionFile>();
        foreach (var f in files)
        {
            var candidate = Path.Combine(newFolder, Path.GetRelativePath(oldFolder, f.Path));
            if (!File.Exists(candidate))
            {
                byName ??= IndexByName(newFolder);
                candidate = byName.TryGetValue(Path.GetFileName(f.Path), out var same) && same.Count == 1 ? same[0] : null!;
            }
            if (candidate is null) { notFound.Add(f); continue; }
            var owner = FindEditionByPath(candidate);
            if (owner == f.EditionId && AppPaths.PathKey(candidate) == AppPaths.PathKey(f.Path)) continue; // already right
            moves.Add(new FileMove(f, candidate, owner is not null && owner != f.EditionId ? owner : null));
        }
        return new FolderMovePlan(oldFolder, newFolder, moves, notFound);
    }

    private static Dictionary<string, List<string>> IndexByName(string folder)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return index;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            var name = Path.GetFileName(path);
            if (!index.TryGetValue(name, out var list)) index[name] = list = [];
            list.Add(path);
        }
        return index;
    }

    /// <summary>
    /// Points every planned file at its new place, keeping each game's settings, favorites and artwork. A duplicate
    /// entry that Add Folder made for the new file is removed first (the original wins). The library folder record
    /// moves too, so rescans use the new place.
    /// </summary>
    public void ApplyFolderMove(FolderMovePlan plan)
    {
        InTransaction(() =>
        {
            foreach (var duplicate in plan.Moves.Select(m => m.DuplicateEditionId).OfType<string>().Distinct())
                Exec("DELETE FROM editions WHERE id = $id", ("$id", duplicate));
            foreach (var m in plan.Moves)
                Exec("UPDATE edition_files SET path = $p, path_key = $k, last_seen_at = $t WHERE id = $id",
                    ("$p", Path.GetFullPath(m.NewPath)), ("$k", AppPaths.PathKey(m.NewPath)), ("$t", UtcNow()), ("$id", m.File.Id));
            // Files not found move along too, to where they'd be in the new folder: still missing, but they belong to
            // the new folder, and copying them there later (or a rescan) finds them.
            foreach (var f in plan.NotFound)
            {
                var expected = Path.Combine(plan.NewFolder, Path.GetRelativePath(plan.OldFolder, f.Path));
                if (FindEditionByPath(expected) is null)
                    Exec("UPDATE edition_files SET path = $p, path_key = $k WHERE id = $id",
                        ("$p", Path.GetFullPath(expected)), ("$k", AppPaths.PathKey(expected)), ("$id", f.Id));
            }

            var oldKey = AppPaths.PathKey(plan.OldFolder);
            var newKey = AppPaths.PathKey(plan.NewFolder);
            using var existing = Cmd("SELECT id FROM library_folders WHERE path_key = $k", ("$k", newKey));
            if (existing.ExecuteScalar() is long newId)
            {
                // The new place was already added as a folder: its files join it, and the old record goes.
                Exec("UPDATE edition_files SET folder_id = $n WHERE folder_id = (SELECT id FROM library_folders WHERE path_key = $o)",
                    ("$n", newId), ("$o", oldKey));
                Exec("DELETE FROM library_folders WHERE path_key = $o", ("$o", oldKey));
            }
            else
                Exec("UPDATE library_folders SET path = $p, path_key = $k WHERE path_key = $o",
                    ("$p", plan.NewFolder), ("$k", newKey), ("$o", oldKey));
        });
    }
}
