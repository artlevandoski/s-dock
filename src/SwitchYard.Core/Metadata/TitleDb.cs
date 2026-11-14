using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwitchYard.Core.Metadata;

/// <summary>One game in TitleDB (the eShop's own listing). Only the fields Switch Yard uses are read.</summary>
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

    /// <summary>How complete the entry is; the best of several listings for one title ID wins.</summary>
    internal int Score =>
        (string.IsNullOrWhiteSpace(Description) ? 0 : 4) + (IconUrl is null ? 0 : 2) + (BannerUrl is null ? 0 : 1) +
        (IsDemo == true ? -8 : 0);
}

[JsonSerializable(typeof(TitleDbEntry))]
internal sealed partial class TitleDbJsonContext : JsonSerializerContext;

/// <summary>
/// TitleDB (github.com/blawar/titledb, MIT): the eShop's names, descriptions, icons, banners and screenshots,
/// listed by title ID, so games match exactly with no account or key. The English (US) file, about 90 MB,
/// is downloaded once into the data folder and refreshed only when asked.
/// </summary>
public sealed class TitleDbClient : IDisposable
{
    public const string SourceUrl = "https://raw.githubusercontent.com/blawar/titledb/master/US.en.json";
    public const string ImageHost = "img-eshop.cdn.nintendo.net";

    private readonly HttpClient _http;

    public TitleDbClient(string folder, HttpMessageHandler? handler = null)
    {
        Folder = folder;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchYard/1.0");
    }

    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, "US.en.json");
    private string ETagPath => FilePath + ".etag";
    public bool HasFile => File.Exists(FilePath);
    public DateTime? Downloaded => HasFile ? File.GetLastWriteTime(FilePath) : null;

    /// <summary>
    /// Downloads TitleDB, or does nothing if the copy here is still current. Returns true when a new copy
    /// was saved. Progress is (bytes so far, total bytes if known).
    /// </summary>
    public async Task<bool> DownloadAsync(IProgress<(long Done, long? Total)>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
        if (HasFile && File.Exists(ETagPath) && File.ReadAllText(ETagPath).Trim() is { Length: > 0 } etag)
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified) return false;
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var temp = FilePath + ".download";
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = File.Create(temp))
        {
            var buffer = new byte[1 << 16];
            long done = 0, lastReport = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (done - lastReport >= 1 << 20) { progress?.Report((done, total)); lastReport = done; }
            }
            progress?.Report((done, total));
        }
        File.Move(temp, FilePath, overwrite: true);
        if (response.Headers.ETag?.ToString() is { } newTag) File.WriteAllText(ETagPath, newTag);
        else if (File.Exists(ETagPath)) File.Delete(ETagPath);
        return true;
    }

    /// <summary>
    /// Finds the given title IDs in the downloaded file (or another TitleDB file). Each entry is read and dropped
    /// in turn, so only the matches are kept in memory. Run it off the UI thread: it takes about a second.
    /// </summary>
    public static Dictionary<string, TitleDbEntry> Lookup(string file, IReadOnlyCollection<string> titleIds)
    {
        var wanted = new HashSet<string>(titleIds, StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, TitleDbEntry>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0 || !File.Exists(file)) return found;
        var bytes = File.ReadAllBytes(file);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("This isn't a TitleDB file.");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }
            var entry = JsonSerializer.Deserialize(ref reader, TitleDbJsonContext.Default.TitleDbEntry);
            if (entry?.Id is not { } id || !wanted.Contains(id)) continue;
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
        IProgress<(int Done, int Total, string Title)>? progress = null, CancellationToken ct = default, bool refresh = false)
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
