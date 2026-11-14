using SwitchYard.Core.Metadata;

namespace SwitchYard.Core;

/// <summary>The newest update file and the DLC files found next to a game.</summary>
public sealed record InstalledContent(long? UpdateVersion, int DlcCount);

/// <summary>
/// Looks beside each game (its folder and up to two levels of subfolders, such as "Updates" and "DLC") for update and
/// DLC files, read from their file names ([title ID] and [vNNN]). Nothing is opened or changed. Each folder is listed
/// once however many games share it.
/// </summary>
public static class LocalContent
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 2,
        IgnoreInaccessible = true,
    };

    public static Dictionary<string, InstalledContent> Index(IEnumerable<Edition> editions)
    {
        var result = new Dictionary<string, InstalledContent>();
        foreach (var group in editions
                     .Where(e => e.PrimaryFile is not null && NxVersions.BaseOf(e.ConsoleTitleId ?? "") is not null)
                     .GroupBy(e => Path.GetDirectoryName(e.PrimaryFile!.Path) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0 || !Directory.Exists(group.Key)) continue;
            // Every update and DLC file in this folder, by the base game it belongs to.
            var updates = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var dlc = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var path in Directory.EnumerateFiles(group.Key, "*", Options))
                {
                    if (!LibraryScanner.GameExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                    var entry = LibraryScanner.ClassifyFile(path);
                    if (entry.TitleId is not { } id || NxVersions.BaseOf(id) is not { } baseId) continue;
                    if (entry.Kind == ContentKinds.Update)
                    {
                        var v = entry.Version ?? 0;
                        if (!updates.TryGetValue(baseId, out var known) || v > known) updates[baseId] = v;
                    }
                    else if (entry.Kind == ContentKinds.Dlc)
                    {
                        if (!dlc.TryGetValue(baseId, out var set)) dlc[baseId] = set = new(StringComparer.OrdinalIgnoreCase);
                        set.Add(id);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var e in group)
            {
                var baseId = NxVersions.BaseOf(e.ConsoleTitleId!)!;
                result[e.Id] = new InstalledContent(updates.TryGetValue(baseId, out var u) ? u : null,
                    dlc.TryGetValue(baseId, out var d) ? d.Count : 0);
            }
        }
        return result;
    }
}
