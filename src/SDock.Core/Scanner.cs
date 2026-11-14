using System.Text.RegularExpressions;

namespace SDock.Core;

/// <summary>Preview flags. Entries with a review flag are not imported by default.</summary>
public static class ScanFlags
{
    public const string Patch = "patch";
    public const string Dlc = "dlc";
    public const string Duplicate = "duplicate";
    public const string AmbiguousName = "ambiguous_name";
    public const string GenerationConflict = "generation_conflict";
    public const string InLibrary = "in_library";

    public static readonly IReadOnlySet<string> Review = new HashSet<string> { Patch, Dlc, Duplicate };

    private static readonly (string Flag, string Label)[] Order =
    [
        (InLibrary, "Already in library"),
        (Patch, "Suspected update/patch"),
        (Dlc, "Suspected DLC"),
        (Duplicate, "Possible duplicate"),
        (AmbiguousName, "Ambiguous filename"),
        (GenerationConflict, "Filename and folder generation disagree"),
    ];

    public static string Describe(IEnumerable<string> flags)
    {
        var set = flags.ToHashSet();
        return string.Join("; ", Order.Where(o => set.Contains(o.Flag)).Select(o => o.Label));
    }
}

public static class GenerationSources
{
    public const string None = "none";
    public const string Folder = "folder";
    public const string Filename = "filename";
    public const string Ambiguous = "ambiguous";
    public const string Conflict = "conflict";
    public const string Manual = "manual";
}

public sealed class ScanEntry
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required string Title { get; set; }
    public string? TitleId { get; init; }
    public long? Version { get; init; }
    public required string Kind { get; init; }
    public string? GenerationHint { get; init; }
    public bool AmbiguousName { get; init; }
    public long SizeBytes { get; init; }
    public string Generation { get; set; } = Generations.Unclassified;
    public string GenerationSource { get; set; } = GenerationSources.None;
    public HashSet<string> Flags { get; } = [];
    public string? ExistingEditionId { get; set; }
    public bool Include { get; set; } = true;

    /// <summary>For a possible duplicate: the library edition it matches (if already imported)…</summary>
    public string? DuplicateOfEditionId { get; set; }
    /// <summary>…or the earlier file in this scan it matches.</summary>
    public string? DuplicateOfPath { get; set; }
    /// <summary>Title of the matched game, for "Add as a version of …".</summary>
    public string? DuplicateOfTitle { get; set; }

    /// <summary>Attach this file to the matched game as an alternate version instead of importing it separately.</summary>
    public bool AddAsVersion { get; set; }

    public bool CanAddAsVersion => Flags.Contains(ScanFlags.Duplicate) && (DuplicateOfEditionId ?? DuplicateOfPath) is not null;

    public bool InLibrary => ExistingEditionId is not null;
    public string Notes => ScanFlags.Describe(Flags);
}

public sealed record ScanResult(string Folder, List<ScanEntry> Entries, List<string> MissingKnown, List<string> Errors)
{
    public IEnumerable<ScanEntry> NewEntries => Entries.Where(e => !e.InLibrary);
}

public sealed record ImportSummary(List<string> Added, int AlreadyPresent, int Skipped, int AddedVersions = 0);

/// <summary>
/// Folder scanning and import preview. Only reads directory listings and file sizes;
/// never opens, renames, moves or modifies game files.
/// </summary>
public static partial class LibraryScanner
{
    public static readonly IReadOnlyList<string> GameExtensions = [".nsp", ".xci", ".nsz", ".xcz"];

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex TitleIdRe();

    [GeneratedRegex(@"\[v(\d+)\]|(?<![A-Za-z0-9])v(\d{5,})(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRe();

    [GeneratedRegex(@"(?<![A-Za-z])(update|upd|patch)(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateWordRe();

    [GeneratedRegex(@"(?<![A-Za-z])(dlc|add[- ]?on)(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex DlcWordRe();

    [GeneratedRegex(@"switch[\s_-]*2(?![0-9])|(?<![A-Za-z0-9])ns2(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Switch2HintRe();

    [GeneratedRegex(@"[\[(]\s*(?:switch[\s_-]*1|ns1)\s*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex Switch1HintRe();

    [GeneratedRegex(@"switch[\s_-]*2\s+edition", RegexOptions.IgnoreCase)]
    private static partial Regex KeepGroupRe();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}")]
    private static partial Regex GroupRe();

    [GeneratedRegex(@"^[0-9A-Fa-f]{8,}$")]
    private static partial Regex HexOnlyRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRe();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlnumRe();

    private static readonly HashSet<string> GenericNames = ["game", "rom", "untitled", "unknown", "file", "new", "copy", "test"];

    public static bool IsGameFile(string path) =>
        GameExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public static string CleanTitle(string stem)
    {
        var s = GroupRe().Replace(stem, m => KeepGroupRe().IsMatch(m.Value) ? " " + m.Value[1..^1] + " " : " ");
        s = s.Replace('_', ' ');
        s = WhitespaceRe().Replace(s, " ").Trim(' ', '-', '.');
        return s;
    }

    public static string NormalizedTitle(string title) => NonAlnumRe().Replace(title.ToLowerInvariant(), "");

    public static bool IsAmbiguousTitle(string title)
    {
        if (title.Count(char.IsLetter) < 2)
            return true;
        if (HexOnlyRe().IsMatch(title.Replace(" ", "")))
            return true;
        return GenericNames.Contains(title.Trim().ToLowerInvariant());
    }

    public static string KindFromTitleId(string titleId)
    {
        var t = titleId.ToUpperInvariant();
        if (t.EndsWith("800", StringComparison.Ordinal)) return ContentKinds.Update;
        if (t.EndsWith("000", StringComparison.Ordinal)) return ContentKinds.Base;
        return ContentKinds.Dlc;
    }

    /// <summary>Generation and its source for an entry the user has not edited.</summary>
    public static (string Generation, string Source) ResolveGeneration(string? hint, bool ambiguous, string folderGeneration)
    {
        if (ambiguous)
            return (Generations.Unclassified, GenerationSources.Ambiguous);
        if (hint is not null && folderGeneration != Generations.Unclassified && hint != folderGeneration)
            return (Generations.Unclassified, GenerationSources.Conflict);
        if (hint is not null)
            return (hint, GenerationSources.Filename);
        if (folderGeneration != Generations.Unclassified)
            return (folderGeneration, GenerationSources.Folder);
        return (Generations.Unclassified, GenerationSources.None);
    }

    public static ScanEntry ClassifyFile(string path, long sizeBytes = 0)
    {
        var fileName = System.IO.Path.GetFileName(path);
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);

        // The file's own ID is the last one: DLC and updates are often named
        // "Game [base ID] - Pack Name [DLC ID] [DLC][v65536]".
        var tidMatches = TitleIdRe().Matches(stem);
        var titleId = tidMatches.Count > 0 ? tidMatches[^1].Groups[1].Value.ToUpperInvariant() : null;
        var verMatch = VersionRe().Match(stem);
        long? version = null;
        if (verMatch.Success)
        {
            var digits = verMatch.Groups[1].Success ? verMatch.Groups[1].Value : verMatch.Groups[2].Value;
            if (long.TryParse(digits, out var v))
                version = v;
        }

        string kind;
        if (titleId is not null) kind = KindFromTitleId(titleId);
        else if (DlcWordRe().IsMatch(stem)) kind = ContentKinds.Dlc;
        else if (UpdateWordRe().IsMatch(stem) || version > 0) kind = ContentKinds.Update;
        else kind = ContentKinds.Base;

        string? hint = null;
        if (Switch2HintRe().IsMatch(stem)) hint = Generations.Switch2;
        else if (Switch1HintRe().IsMatch(stem)) hint = Generations.Switch1;

        var title = CleanTitle(stem);
        if (title.Length == 0) title = stem;
        var ambiguous = IsAmbiguousTitle(title);

        var entry = new ScanEntry
        {
            Path = System.IO.Path.GetFullPath(path),
            FileName = fileName,
            Title = title,
            TitleId = titleId,
            Version = version,
            Kind = kind,
            GenerationHint = hint,
            AmbiguousName = ambiguous,
            SizeBytes = sizeBytes,
        };
        if (kind == ContentKinds.Update) entry.Flags.Add(ScanFlags.Patch);
        else if (kind == ContentKinds.Dlc) entry.Flags.Add(ScanFlags.Dlc);
        if (ambiguous) entry.Flags.Add(ScanFlags.AmbiguousName);
        return entry;
    }

    /// <summary>(Re)assign the folder generation to every entry the user hasn't edited.</summary>
    public static void ApplyFolderGeneration(IEnumerable<ScanEntry> entries, string folderGeneration)
    {
        foreach (var e in entries)
        {
            if (e.GenerationSource == GenerationSources.Manual)
                continue;
            (e.Generation, e.GenerationSource) = ResolveGeneration(e.GenerationHint, e.AmbiguousName, folderGeneration);
            if (e.GenerationSource == GenerationSources.Conflict)
                e.Flags.Add(ScanFlags.GenerationConflict);
            else
                e.Flags.Remove(ScanFlags.GenerationConflict);
        }
    }

    /// <summary>Files first, then subfolders, each sorted case-insensitively.</summary>
    private static IEnumerable<string> EnumerateGameFiles(string folder, bool recursive, List<string> errors)
    {
        string[] files, dirs;
        try
        {
            files = Directory.GetFiles(folder);
            dirs = recursive ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            errors.Add($"{folder}: {ex.Message}");
            yield break;
        }
        foreach (var f in files.Where(IsGameFile).Order(StringComparer.OrdinalIgnoreCase))
            yield return f;
        foreach (var d in dirs.Order(StringComparer.OrdinalIgnoreCase))
            foreach (var f in EnumerateGameFiles(d, recursive, errors))
                yield return f;
    }

    private static string TitleKey(string title, string generation) => $"title:{generation}:{NormalizedTitle(title)}";

    public static ScanResult ScanFolder(string folder, string folderGeneration = Generations.Unclassified,
        LibraryStore? library = null, bool recursive = true)
    {
        folder = System.IO.Path.GetFullPath(folder);
        var errors = new List<string>();
        var entries = new List<ScanEntry>();
        if (!Directory.Exists(folder))
            return new ScanResult(folder, entries, [], [$"Folder not found: {folder}"]);

        foreach (var path in EnumerateGameFiles(folder, recursive, errors))
        {
            long size;
            try { size = new FileInfo(path).Length; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                errors.Add($"{path}: {ex.Message}");
                continue;
            }
            entries.Add(ClassifyFile(path, size));
        }

        ApplyFolderGeneration(entries, folderGeneration);

        // Duplicate detection: against existing library editions, then within the scan.
        // Title matches only count within one generation, so a Switch 2 edition of a game is never
        // treated as a duplicate of the Switch 1 one (they stay separate by default).
        var seen = new Dictionary<string, (string? EditionId, string? Path, string Title)>();
        if (library is not null)
        {
            foreach (var ed in library.ListEditions().Where(e => e.ContentKind == ContentKinds.Base))
            {
                if (ed.ConsoleTitleId is { } tid)
                    seen.TryAdd("tid:" + tid.ToUpperInvariant(), (ed.Id, null, ed.Title));
                seen.TryAdd(TitleKey(ed.Title, ed.Generation), (ed.Id, null, ed.Title));
            }
        }

        foreach (var e in entries)
        {
            if (library is not null)
                e.ExistingEditionId = library.FindEditionByPath(e.Path);
            if (e.InLibrary)
            {
                e.Flags.Add(ScanFlags.InLibrary);
                e.Include = false;
                continue;
            }
            if (e.Kind == ContentKinds.Base)
            {
                var keys = new List<string>();
                if (e.TitleId is not null) keys.Add("tid:" + e.TitleId);
                if (!e.AmbiguousName) keys.Add(TitleKey(e.Title, e.Generation));
                if (keys.Select(k => seen.TryGetValue(k, out var m) ? m : default).FirstOrDefault(m => m.Title is not null) is { Title: not null } match)
                {
                    e.Flags.Add(ScanFlags.Duplicate);
                    e.DuplicateOfEditionId = match.EditionId;
                    e.DuplicateOfPath = match.Path;
                    e.DuplicateOfTitle = match.Title;
                }
                foreach (var k in keys) seen.TryAdd(k, (null, e.Path, e.Title));
            }
            e.Include = !e.Flags.Overlaps(ScanFlags.Review);
        }

        var missing = library?.FilesUnder(folder).Where(f => !f.Exists).Select(f => f.Path).ToList() ?? [];
        return new ScanResult(folder, entries, missing, errors);
    }

    /// <summary>Write the chosen preview entries to the library. Existing records are preserved.</summary>
    public static ImportSummary ImportEntries(LibraryStore library, string folder, string folderGeneration, IEnumerable<ScanEntry> entries)
    {
        ImportSummary? result = null;
        library.InTransaction(() => result = ImportEntriesCore(library, folder, folderGeneration, entries));
        return result!;
    }

    private static ImportSummary ImportEntriesCore(LibraryStore library, string folder, string folderGeneration, IEnumerable<ScanEntry> entries)
    {
        var folderId = library.UpsertFolder(folder, folderGeneration);
        var added = new List<string>();
        var present = new List<string>();
        var versions = new List<ScanEntry>();
        var skipped = 0;
        var list = entries.ToList();
        foreach (var e in list)
        {
            // Re-check at import time so repeat imports can never create duplicates.
            var existing = e.ExistingEditionId ?? library.FindEditionByPath(e.Path);
            if (existing is not null) { present.Add(e.Path); continue; }
            if (e.AddAsVersion && e.CanAddAsVersion) { versions.Add(e); continue; }
            if (!e.Include) { skipped++; continue; }
            added.Add(library.AddEdition(e.Title, e.Generation, e.Path, e.Kind, e.TitleId, folderId, e.SizeBytes));
        }

        // Versions attach after the games they belong to exist (the match may be earlier in this scan).
        var addedVersions = 0;
        foreach (var e in versions)
        {
            var target = e.DuplicateOfEditionId ?? (e.DuplicateOfPath is { } p ? library.FindEditionByPath(p) : null);
            if (target is null) { skipped++; continue; } // its game wasn't imported
            library.AddFile(target, e.Path, e.SizeBytes);
            addedVersions++;
        }
        library.MarkSeen(present);
        return new ImportSummary(added, present.Count, skipped, addedVersions);
    }
}
