using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDock.Core.Metadata;

/// <summary>One game in TitleDB (the eShop's own listing). Only the fields S-Dock uses are read.</summary>
public sealed record TitleDbEntry
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("intro")] public string? Intro { get; init; }
    [JsonPropertyName("publisher")] public string? Publisher { get; init; }
    [JsonPropertyName("developer")] public string? Developer { get; init; }
    [JsonPropertyName("releaseDate")] public int? ReleaseDate { get; init; } // yyyymmdd
    [JsonPropertyName("category")] public List<string>? Category { get; init; }
    [JsonPropertyName("numberOfPlayers")] public int? NumberOfPlayers { get; init; }
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; init; }
    [JsonPropertyName("bannerUrl")] public string? BannerUrl { get; init; }
    [JsonPropertyName("screenshots")] public List<string>? Screenshots { get; init; }
    [JsonPropertyName("isDemo")] public bool? IsDemo { get; init; }

    /// <summary>Which listing it came from (S-Dock's own field in the merged file).</summary>
    [JsonPropertyName("syRegion")] public string? SourceRegion { get; init; }

    /// <summary>How complete the entry is; the best of several listings for one title ID wins.</summary>
    internal int Score =>
        (string.IsNullOrWhiteSpace(Description) ? 0 : 4) + (IconUrl is null ? 0 : 2) + (BannerUrl is null ? 0 : 1) +
        (IsDemo == true ? -8 : 0);
}

[JsonSerializable(typeof(TitleDbEntry))]
internal sealed partial class TitleDbJsonContext : JsonSerializerContext;

/// <summary>Progress of a TitleDB download: which file, and bytes so far.</summary>
public readonly record struct TitleDbProgress(string File, int Index, int Count, long Done, long? Total);

/// <summary>
/// TitleDB (MIT): the eShop's names, descriptions, icons, banners and screenshots, listed by title ID, so games
/// match exactly with no account or key. S-Dock reads the US English listing first and fills gaps from the
/// UK, Australian and Japanese listings (newer or region-only games), then merges them into one compact file in
/// the data folder. The listings come from AeroFoil's fork (luketanti/titledb), which is kept more up to date;
/// the original (blawar/titledb) is used for any file the fork can't provide.
/// </summary>
public sealed class TitleDbClient : IDisposable
{
    public const string ForkBase = "https://raw.githubusercontent.com/luketanti/titledb/master/";
    public const string UpstreamBase = "https://raw.githubusercontent.com/blawar/titledb/master/";
    public const string ImageHost = "img-eshop.cdn.nintendo.net";

    /// <summary>Listings in order of preference. Japanese only lends pictures, names and dates (no Japanese text).</summary>
    public static readonly IReadOnlyList<string> Regions = ["US.en", "GB.en", "AU.en", "JP.ja"];

    private readonly HttpClient _http;

    public TitleDbClient(string folder, HttpMessageHandler? handler = null)
    {
        Folder = folder;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("S-Dock/1.0");
    }

    public string Folder { get; }

    /// <summary>The merged listing: one entry per title ID, only the fields S-Dock uses.</summary>
    public string FilePath => Path.Combine(Folder, "titles.json");
    private string ETagsPath => Path.Combine(Folder, "titles.etags");
    public bool HasFile => File.Exists(FilePath);
    public DateTime? Downloaded => HasFile ? File.GetLastWriteTime(FilePath) : null;

    internal HttpClient Http => _http;

    /// <summary>
    /// Downloads the listings and rebuilds the merged file, or does nothing if none of them changed since last
    /// time. Returns true when a new merged file was saved.
    /// </summary>
    public async Task<bool> DownloadAsync(IProgress<TitleDbProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var known = ReadETags();
        // First a quick look: has anything changed? (Each listing is ~90 MB, so don't fetch them for nothing.)
        if (HasFile && known.Count == Regions.Count)
        {
            var unchanged = true;
            foreach (var region in Regions)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ForkBase + region + ".json");
                if (known.TryGetValue(region, out var tag)) request.Headers.TryAddWithoutValidation("If-None-Match", tag);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.NotModified) { unchanged = false; break; }
            }
            if (unchanged) return false;
        }

        var merged = new Dictionary<string, TitleDbEntry>(StringComparer.OrdinalIgnoreCase);
        var etags = new Dictionary<string, string>();
        for (var i = 0; i < Regions.Count; i++)
        {
            var region = Regions[i];
            var temp = Path.Combine(Folder, region + ".json.download");
            try
            {
                var tag = await DownloadFileAsync(region, temp, i, progress, ct).ConfigureAwait(false);
                if (tag is not null) etags[region] = tag;
                Merge(merged, temp, region);
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        }
        WriteMerged(merged, FilePath);
        File.WriteAllLines(ETagsPath, etags.Select(kv => kv.Key + "\t" + kv.Value));
        foreach (var old in new[] { "US.en.json", "US.en.json.etag" }) // the single-listing file of earlier versions
            try { File.Delete(Path.Combine(Folder, old)); } catch (IOException) { }
        return true;
    }

    private Dictionary<string, string> ReadETags()
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(ETagsPath)) return result;
        foreach (var line in File.ReadAllLines(ETagsPath))
            if (line.Split('\t') is [var region, var tag]) result[region] = tag;
        return result;
    }

    /// <summary>Streams one listing to disk, from the fork or else the original. Returns its ETag.</summary>
    private async Task<string?> DownloadFileAsync(string region, string target, int index, IProgress<TitleDbProgress>? progress,
        CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        foreach (var source in new[] { ForkBase, UpstreamBase })
        {
            try
            {
                response = await _http.GetAsync(source + region + ".json", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) when (source == ForkBase) { }
            response?.Dispose();
            response = null;
        }
        if (response is null) throw new HttpRequestException($"TitleDB's {region} listing couldn't be downloaded.");
        using (response)
        {
            var total = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = File.Create(target);
            var buffer = new byte[1 << 16];
            long done = 0, lastReport = -1;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (done - lastReport >= 1 << 20) { progress?.Report(new(region, index, Regions.Count, done, total)); lastReport = done; }
            }
            progress?.Report(new(region, index, Regions.Count, done, total));
            return response.Headers.ETag?.ToString();
        }
    }

    /// <summary>Adds a listing's games that aren't in the merged set yet (earlier listings win).</summary>
    internal static void Merge(Dictionary<string, TitleDbEntry> merged, string file, string region)
    {
        var textToo = region.EndsWith(".en", StringComparison.Ordinal);
        var fromThisFile = new Dictionary<string, TitleDbEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadEntries(file))
        {
            if (entry.Id is not { Length: 16 } id || merged.ContainsKey(id)) continue;
            var e = textToo ? entry with { SourceRegion = region } : entry with { Description = null, Intro = null, SourceRegion = region };
            if (!fromThisFile.TryGetValue(id, out var current) || e.Score > current.Score)
                fromThisFile[id] = e;
        }
        foreach (var (id, e) in fromThisFile) merged[id] = e;
    }

    private static IEnumerable<TitleDbEntry> ReadEntries(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var list = new List<TitleDbEntry>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("This isn't a TitleDB file.");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); continue; }
            if (JsonSerializer.Deserialize(ref reader, TitleDbJsonContext.Default.TitleDbEntry) is { } entry)
                list.Add(entry);
        }
        return list;
    }

    private static void WriteMerged(Dictionary<string, TitleDbEntry> merged, string path)
    {
        var temp = path + ".download";
        using (var stream = File.Create(temp))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (id, entry) in merged.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(id);
                JsonSerializer.Serialize(writer, entry, TitleDbJsonContext.Default.TitleDbEntry);
            }
            writer.WriteEndObject();
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Finds the given title IDs in the merged file (or any TitleDB listing). Run it off the UI thread: reading
    /// the file takes about a second.
    /// </summary>
    public static Dictionary<string, TitleDbEntry> Lookup(string file, IReadOnlyCollection<string> titleIds)
    {
        var wanted = new HashSet<string>(titleIds, StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, TitleDbEntry>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0 || !File.Exists(file)) return found;
        foreach (var entry in ReadEntries(file))
        {
            if (entry.Id is not { } id || !wanted.Contains(id)) continue;
            if (!found.TryGetValue(id, out var current) || entry.Score > current.Score)
                found[id] = entry;
        }
        return found;
    }

    /// <summary>Downloads an eShop image. Only Nintendo's image server is accepted.</summary>
    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(ImageHost, StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException($"Refused an image from an unexpected address: {url}");
        return await _http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record TitleDbSummary(int Found, int NotFound, int ImagesFailed, bool Cancelled);

/// <summary>Applies TitleDB to games: text into the metadata, the icon and banner into the artwork folder.</summary>
public sealed class TitleDbService(LibraryStore library, TitleDbClient client, string artworkDir)
{
    public const string IconName = "tdb-icon.jpg";
    public const string BannerName = "tdb-banner.jpg";
    private const int Parallel = 6;

    /// <summary>
    /// Looks up each game by its title ID and saves what TitleDB has. Images already downloaded are kept unless
    /// <paramref name="refresh"/>. Runs on the caller's thread except the file scan and downloads, so the database
    /// is only used from one thread. Progress is (done, total, title).
    /// </summary>
    public async Task<TitleDbSummary> ApplyAsync(IReadOnlyList<Edition> editions,
        IProgress<(int Done, int Total, string Title)>? progress = null, CancellationToken ct = default, bool refresh = false,
        IReadOnlyDictionary<string, NxVersionInfo>? versions = null)
    {
        var withId = editions.Where(e => BaseTitleId(e.ConsoleTitleId) is not null).ToList();
        var ids = withId.Select(e => BaseTitleId(e.ConsoleTitleId)!).Distinct().ToList();
        progress?.Report((0, withId.Count, "Reading TitleDB"));
        var file = client.FilePath;
        var entries = await Task.Run(() => TitleDbClient.Lookup(file, ids), ct);
        int found = 0, imagesFailed = 0, done = 0;
        var notFound = editions.Count - withId.Count;

        foreach (var batch in withId.Chunk(Parallel))
        {
            if (ct.IsCancellationRequested) return new TitleDbSummary(found, notFound, imagesFailed, true);
            var work = batch.Select(async e =>
            {
                if (!entries.TryGetValue(BaseTitleId(e.ConsoleTitleId)!, out var entry)) return (e, (TitleDbEntry?)null, (string?)null, (string?)null, 0);
                var existing = library.GetMetadata(e.Id);
                var dir = Path.Combine(artworkDir, e.Id);
                var (icon, f1) = await SaveImageAsync(dir, IconName, entry.IconUrl, existing.TdbIconFile, refresh, ct);
                var (banner, f2) = await SaveImageAsync(dir, BannerName, entry.BannerUrl, existing.TdbBannerFile, refresh, ct);
                return (e, entry, icon, banner, f1 + f2);
            }).ToList();
            foreach (var (e, entry, icon, banner, failed) in await Task.WhenAll(work))
            {
                done++;
                progress?.Report((done, withId.Count, e.Title));
                imagesFailed += failed;
                var nx = versions is not null && versions.TryGetValue(BaseTitleId(e.ConsoleTitleId)!, out var v) ? v : null;
                if (nx is not null)
                    library.SaveMetadata(library.GetMetadata(e.Id) with
                    {
                        NxLatestUpdate = nx.LatestUpdate > 0 ? nx.LatestUpdate : null,
                        NxUpdateDate = nx.LatestUpdate > 0 ? nx.LatestUpdateDate : null,
                        NxFirstDate = nx.FirstSeenDate,
                    });
                if (entry is null) { notFound++; continue; }
                found++;
                library.SaveMetadata(library.GetMetadata(e.Id) with
                {
                    TdbTitleId = entry.Id,
                    TdbName = Clean(entry.Name),
                    TdbSummary = Clean(entry.Description),
                    TdbPublisher = Clean(entry.Publisher) ?? Clean(entry.Developer),
                    TdbReleaseDate = entry.ReleaseDate is > 19000000 ? entry.ReleaseDate : null,
                    TdbGenres = (entry.Category ?? []).Where(c => !string.IsNullOrWhiteSpace(c) && c != "Other").Distinct().ToList(),
                    TdbPlayers = entry.NumberOfPlayers is > 0 ? entry.NumberOfPlayers : null,
                    TdbIconFile = icon,
                    TdbBannerFile = banner,
                    TdbArtUrls = new[] { entry.BannerUrl }.Concat(entry.Screenshots ?? []).OfType<string>().Distinct().ToList(),
                    TdbFetchedAt = LibraryStore.UtcNow(),
                });
            }
        }
        return new TitleDbSummary(found, notFound, imagesFailed, false);
    }

    /// <summary>The base game's title ID for a game, update or DLC ID (updates end in 800; DLC is a different scheme).</summary>
    public static string? BaseTitleId(string? titleId)
    {
        if (titleId is not { Length: 16 } || !titleId.All(Uri.IsHexDigit)) return null;
        var id = titleId.ToUpperInvariant();
        return id.EndsWith("800") ? id[..13] + "000" : id;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <returns>The relative file to use (or null) and 1 if a download failed.</returns>
    private async Task<(string? File, int Failed)> SaveImageAsync(string dir, string name, string? url, string? existing,
        bool refresh, CancellationToken ct)
    {
        if (existing is not null && !refresh && File.Exists(Path.Combine(artworkDir, existing))) return (existing, 0);
        if (string.IsNullOrEmpty(url)) return (null, 0);
        try
        {
            var bytes = await client.DownloadImageAsync(url, ct);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, name);
            await ArtFiles.WriteAsync(target, bytes, ct);
            return (Path.GetRelativePath(artworkDir, target), 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (existing, 1); // keep what we had; try again next time
        }
    }
}
