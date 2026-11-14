using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SwitchYard.Core.Metadata;

/// <summary>
/// What nx-versions knows about a game: its newest update version, when that update first appeared, and when
/// anything for the game first appeared (a stand-in release date when TitleDB has none; it may be a little late).
/// </summary>
public sealed record NxVersionInfo(long LatestUpdate, int? LatestUpdateDate, int? FirstSeenDate);

/// <summary>
/// nx-versions (github.com/16BitWonder/nx-versions): the latest known update version of each game, and a changelog
/// of when each version appeared. No names or artwork. Kept in the TitleDB folder; if a refresh fails, the copy
/// already downloaded is used.
/// </summary>
public static partial class NxVersions
{
    public const string VersionsUrl = "https://raw.githubusercontent.com/16BitWonder/nx-versions/master/versions.txt";
    public const string ChangelogUrl = "https://raw.githubusercontent.com/16BitWonder/nx-versions/master/FULL-CHANGELOG.md";

    public static string VersionsPath(string folder) => Path.Combine(folder, "nx-versions.txt");
    public static string ChangelogPath(string folder) => Path.Combine(folder, "nx-versions-changelog.md");
    public static bool HasFiles(string folder) => File.Exists(VersionsPath(folder));

    /// <summary>Downloads both files when they've changed. Returns false (keeping the old copy) if that fails.</summary>
    public static async Task<bool> DownloadAsync(TitleDbClient client, CancellationToken ct = default)
    {
        Directory.CreateDirectory(client.Folder);
        try
        {
            await DownloadOneAsync(client.Http, VersionsUrl, VersionsPath(client.Folder), ct).ConfigureAwait(false);
            await DownloadOneAsync(client.Http, ChangelogUrl, ChangelogPath(client.Folder), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task DownloadOneAsync(HttpClient http, string url, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var etagPath = path + ".etag";
        if (File.Exists(path) && File.Exists(etagPath))
            request.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(etagPath).Trim());
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified) return;
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        await ArtFiles.WriteAsync(path, bytes, ct).ConfigureAwait(false);
        if (response.Headers.ETag?.ToString() is { } tag) File.WriteAllText(etagPath, tag);
    }

    [GeneratedRegex(@"^##\s+(\d{4})-(\d{2})-(\d{2})")]
    private static partial Regex DateLineRe();

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]\[v(\d+)\](?:->\[v(\d+)\])?")]
    private static partial Regex EntryRe();

    /// <summary>Reads the downloaded files into one entry per base game (by its title ID).</summary>
    public static Dictionary<string, NxVersionInfo> Load(string folder)
    {
        var result = new Dictionary<string, NxVersionInfo>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(VersionsPath(folder))) return result;

        var latest = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(VersionsPath(folder)))
        {
            if (line.Split('|') is not [var id, var ver] || id.Length != 16 || !long.TryParse(ver, out var v)) continue;
            if (!id.EndsWith("800", StringComparison.OrdinalIgnoreCase)) continue; // updates only; DLC isn't tracked here
            var baseId = id[..13].ToUpperInvariant() + "000";
            if (!latest.TryGetValue(baseId, out var known) || v > known) latest[baseId] = v;
        }

        // The changelog is newest first; keep the earliest date seen for each game and each update version.
        var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var versionDate = new Dictionary<(string, long), int>();
        if (File.Exists(ChangelogPath(folder)))
        {
            int? date = null;
            foreach (var line in File.ReadLines(ChangelogPath(folder)))
            {
                if (DateLineRe().Match(line) is { Success: true } d)
                {
                    date = int.Parse(d.Groups[1].Value + d.Groups[2].Value + d.Groups[3].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (date is not { } day || EntryRe().Match(line) is not { Success: true } m) continue;
                var id = m.Groups[1].Value.ToUpperInvariant();
                var baseId = BaseOf(id);
                if (baseId is null) continue;
                var version = long.Parse(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!firstSeen.TryGetValue(baseId, out var seen) || day < seen) firstSeen[baseId] = day;
                if (id.EndsWith("800", StringComparison.Ordinal) &&
                    (!versionDate.TryGetValue((baseId, version), out var vd) || day < vd))
                    versionDate[(baseId, version)] = day;
            }
        }

        foreach (var (baseId, version) in latest)
            result[baseId] = new NxVersionInfo(version,
                versionDate.TryGetValue((baseId, version), out var vd) ? vd : null,
                firstSeen.TryGetValue(baseId, out var fs) ? fs : null);
        foreach (var (baseId, fs) in firstSeen.Where(kv => !result.ContainsKey(kv.Key)))
            result[baseId] = new NxVersionInfo(0, null, fs);
        return result;
    }

    /// <summary>The base game of a game, update (…800) or DLC (next block up, …001 and so on) title ID.</summary>
    public static string? BaseOf(string titleId)
    {
        if (titleId.Length != 16 || !ulong.TryParse(titleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return null;
        var low = id & 0xFFF;
        var baseId = low == 0 || low == 0x800 ? id & ~0xFFFUL : (id & ~0xFFFUL) - 0x1000;
        return baseId.ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>Switch update numbers count in steps of 65536: v262144 is the 4th update.</summary>
    public static string Describe(long version) =>
        version == 0 ? "v0" : version % 65536 == 0 ? $"update {version / 65536} (v{version})" : $"v{version}";
}
