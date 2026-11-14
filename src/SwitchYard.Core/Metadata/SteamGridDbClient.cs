using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwitchYard.Core.Metadata;

public sealed record SgdbGame
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("verified")] public bool Verified { get; init; }
}

/// <summary>A community image on SteamGridDB (a clear logo or a hero/background).</summary>
public sealed record SgdbImage
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("score")] public int Score { get; init; }
    [JsonPropertyName("style")] public string Style { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("thumb")] public string Thumb { get; init; } = "";
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("mime")] public string Mime { get; init; } = "";
    [JsonPropertyName("language")] public string? Language { get; init; }
    [JsonPropertyName("upvotes")] public int Upvotes { get; init; }
}

internal sealed record SgdbResponse<T>
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("data")] public List<T>? Data { get; init; }
}

[JsonSerializable(typeof(SgdbResponse<SgdbGame>))]
[JsonSerializable(typeof(SgdbResponse<SgdbImage>))]
internal sealed partial class SgdbJsonContext : JsonSerializerContext;

/// <summary>
/// SteamGridDB v2: community clear logos and hero (background) images. Each user supplies their own
/// free API key (steamgriddb.com → Preferences → API); nothing secret ships with Switch Yard.
/// </summary>
public sealed class SteamGridDbClient : IDisposable
{
    public const string ApiBase = "https://www.steamgriddb.com/api/v2/";

    private readonly HttpClient _http;

    public SteamGridDbClient(string apiKey, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchYard/0.1");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _http.Dispose();

    private async Task<List<T>> GetAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<SgdbResponse<T>> type,
        CancellationToken ct)
    {
        using var resp = await _http.GetAsync(ApiBase + path, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new IgdbException("SteamGridDB didn't accept the API key. Check it in Metadata Settings.", resp.StatusCode);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return [];
        if (!resp.IsSuccessStatusCode)
            throw new IgdbException($"SteamGridDB request failed ({(int)resp.StatusCode}).", resp.StatusCode);
        var body = JsonSerializer.Deserialize(await resp.Content.ReadAsStringAsync(ct), type);
        return body?.Data ?? [];
    }

    public Task<List<SgdbGame>> SearchAsync(string title, CancellationToken ct = default) =>
        GetAsync("search/autocomplete/" + Uri.EscapeDataString(title.Trim()), SgdbJsonContext.Default.SgdbResponseSgdbGame, ct);

    /// <summary>Clear logos (transparent PNGs), best first.</summary>
    public async Task<List<SgdbImage>> LogosAsync(long gameId, CancellationToken ct = default) =>
        Rank(await GetAsync($"logos/game/{gameId}?types=static&nsfw=false&humor=false", SgdbJsonContext.Default.SgdbResponseSgdbImage, ct));

    /// <summary>Heroes: wide background images, best first.</summary>
    public async Task<List<SgdbImage>> HeroesAsync(long gameId, CancellationToken ct = default) =>
        Rank(await GetAsync($"heroes/game/{gameId}?types=static&nsfw=false&humor=false", SgdbJsonContext.Default.SgdbResponseSgdbImage, ct));

    /// <summary>Official art first, then English, then the community's favourites.</summary>
    public static List<SgdbImage> Rank(IEnumerable<SgdbImage> images) =>
        images.OrderByDescending(i => i.Style == "official")
              .ThenByDescending(i => i.Language is null or "en")
              .ThenByDescending(i => i.Score)
              .ThenByDescending(i => i.Upvotes)
              .ToList();

    /// <summary>The SteamGridDB game with exactly this title (loosely compared), preferring verified entries.</summary>
    public async Task<SgdbGame?> FindGameAsync(string title, CancellationToken ct = default)
    {
        var key = MetadataService.MatchKey(title);
        var results = await SearchAsync(title, ct);
        return results.Where(g => MetadataService.MatchKey(g.Name) == key)
                      .OrderByDescending(g => g.Verified)
                      .FirstOrDefault();
    }

    public async Task<byte[]> DownloadAsync(string url, CancellationToken ct = default)
    {
        // Only SteamGridDB's own image hosts.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.EndsWith("steamgriddb.com", StringComparison.OrdinalIgnoreCase)))
            throw new IgdbException("Unexpected image address from SteamGridDB.");
        using var resp = await _http.GetAsync(uri, ct);
        if (!resp.IsSuccessStatusCode)
            throw new IgdbException($"Couldn't download the image ({(int)resp.StatusCode}).", resp.StatusCode);
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task TestAsync(CancellationToken ct = default) => await SearchAsync("Mario", ct);
}
