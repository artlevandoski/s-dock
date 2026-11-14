using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SwitchYard.Core.Metadata;

/// <summary>A game as returned by IGDB (only the fields Switch Yard uses).</summary>
public sealed record IgdbGame
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("slug")] public string? Slug { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("first_release_date")] public long? FirstReleaseDate { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("genres")] public List<IgdbNamed>? Genres { get; init; }
    [JsonPropertyName("platforms")] public List<IgdbNamed>? Platforms { get; init; }
    [JsonPropertyName("cover")] public IgdbImage? Cover { get; init; }
    [JsonPropertyName("artworks")] public List<IgdbImage>? Artworks { get; init; }
    [JsonPropertyName("screenshots")] public List<IgdbImage>? Screenshots { get; init; }

    public int? ReleaseYear => FirstReleaseDate is { } s ? DateTimeOffset.FromUnixTimeSeconds(s).Year : null;
    public IEnumerable<string> GenreNames => Genres?.Select(g => g.Name).Where(n => n.Length > 0) ?? [];
    public IEnumerable<string> PlatformNames => Platforms?.Select(p => p.Abbreviation ?? p.Name).Where(n => n.Length > 0) ?? [];

    /// <summary>Landscape key art: an artwork if IGDB has one, otherwise a screenshot.</summary>
    public string? KeyArtImageId => Artworks?.FirstOrDefault()?.ImageId ?? Screenshots?.FirstOrDefault()?.ImageId;
}

public sealed record IgdbNamed
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("abbreviation")] public string? Abbreviation { get; init; }
}

public sealed record IgdbImage
{
    [JsonPropertyName("image_id")] public string ImageId { get; init; } = "";
}

internal sealed record TwitchToken
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("expires_in")] public long ExpiresIn { get; init; }
}

[JsonSerializable(typeof(List<IgdbGame>))]
[JsonSerializable(typeof(List<IgdbNamed>))]
[JsonSerializable(typeof(TwitchToken))]
internal sealed partial class IgdbJsonContext : JsonSerializerContext;

public sealed class IgdbException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Minimal IGDB v4 client. Authenticates with the user's own Twitch application credentials
/// (client-credentials flow); nothing secret ships with Switch Yard. Requests are spaced to stay
/// within IGDB's limit of four per second.
/// </summary>
public sealed partial class IgdbClient : IDisposable
{
    public const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    public const string ApiBase = "https://api.igdb.com/v4/";
    public const string ImageBase = "https://images.igdb.com/igdb/image/upload/";
    public const string GameFields =
        "id,name,slug,summary,first_release_date,url,genres.name,platforms.name,platforms.abbreviation," +
        "cover.image_id,artworks.image_id,screenshots.image_id";

    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(260);

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private Dictionary<string, long>? _platformIds;

    public IgdbClient(string clientId, string clientSecret, HttpMessageHandler? handler = null)
    {
        _clientId = clientId.Trim();
        _clientSecret = clientSecret.Trim();
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchYard/0.1");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>For tests: skip the request spacing.</summary>
    public bool Throttle { get; set; } = true;

    private async Task<string> TokenAsync(CancellationToken ct, bool forceNew = false)
    {
        if (!forceNew && _token is not null && DateTimeOffset.UtcNow < _tokenExpires)
            return _token;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "client_credentials",
        });
        using var resp = await _http.PostAsync(TokenUrl, form, ct);
        if (!resp.IsSuccessStatusCode)
            throw new IgdbException(resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Twitch didn't accept the Client ID and Client Secret. Check them in IGDB Settings."
                : $"Couldn't sign in to Twitch ({(int)resp.StatusCode}).", resp.StatusCode);
        var token = JsonSerializer.Deserialize(await resp.Content.ReadAsStringAsync(ct), IgdbJsonContext.Default.TwitchToken);
        if (token is null || token.AccessToken.Length == 0)
            throw new IgdbException("Twitch returned no access token.");
        _token = token.AccessToken;
        _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 300)); // renew early
        return _token;
    }

    /// <summary>POSTs an Apicalypse query to an IGDB endpoint and returns the raw JSON.</summary>
    public async Task<string> QueryAsync(string endpoint, string body, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (Throttle)
                {
                    var wait = _lastRequest + MinInterval - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                }
                var token = await TokenAsync(ct, forceNew: attempt > 0 && _token is null);
                using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain"),
                };
                req.Headers.Add("Client-ID", _clientId);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await _http.SendAsync(req, ct);
                _lastRequest = DateTimeOffset.UtcNow;

                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null; // expired or revoked: get a new token once
                    continue;
                }
                if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 + attempt), ct);
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                    throw new IgdbException($"IGDB request failed ({(int)resp.StatusCode}).", resp.StatusCode);
                return await resp.Content.ReadAsStringAsync(ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<IgdbGame>> GamesAsync(string body, CancellationToken ct) =>
        JsonSerializer.Deserialize(await QueryAsync("games", body, ct), IgdbJsonContext.Default.ListIgdbGame) ?? [];

    /// <summary>Text for use inside an Apicalypse string literal.</summary>
    public static string Escape(string text) => text.Replace("\\", " ").Replace("\"", " ").Trim();

    /// <summary>IGDB platform IDs for "Nintendo Switch" and "Nintendo Switch 2", looked up once.</summary>
    public async Task<IReadOnlyDictionary<string, long>> SwitchPlatformsAsync(CancellationToken ct = default)
    {
        if (_platformIds is not null) return _platformIds;
        var json = await QueryAsync("platforms", "fields id,name; where name = (\"Nintendo Switch\",\"Nintendo Switch 2\"); limit 10;", ct);
        var found = JsonSerializer.Deserialize(json, IgdbJsonContext.Default.ListIgdbNamed) ?? [];
        var ids = new Dictionary<string, long>();
        foreach (var p in found)
        {
            if (p.Name == "Nintendo Switch") ids[Generations.Switch1] = p.Id;
            else if (p.Name == "Nintendo Switch 2") ids[Generations.Switch2] = p.Id;
        }
        return _platformIds = ids;
    }

    /// <summary>Search by title, limited to the given IGDB platforms (none = any platform).</summary>
    public async Task<List<IgdbGame>> SearchAsync(string title, IEnumerable<long> platformIds, int limit = 15, CancellationToken ct = default)
    {
        var q = Escape(title);
        if (q.Length == 0) return [];
        var platforms = platformIds.ToList();
        var where = platforms.Count > 0 ? $" where platforms = ({string.Join(",", platforms)});" : "";
        return await GamesAsync($"search \"{q}\"; fields {GameFields};{where} limit {limit};", ct);
    }

    public async Task<IgdbGame?> GetByIdAsync(long id, CancellationToken ct = default) =>
        (await GamesAsync($"fields {GameFields}; where id = {id}; limit 1;", ct)).FirstOrDefault();

    public async Task<IgdbGame?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        (await GamesAsync($"fields {GameFields}; where slug = \"{Escape(slug)}\"; limit 1;", ct)).FirstOrDefault();

    /// <summary>Checks the credentials by signing in and making one small request.</summary>
    public async Task TestAsync(CancellationToken ct = default) => await SwitchPlatformsAsync(ct);

    public async Task<byte[]> DownloadImageAsync(string imageId, string size, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(ImageUrl(imageId, size), ct);
        if (!resp.IsSuccessStatusCode)
            throw new IgdbException($"Couldn't download artwork ({(int)resp.StatusCode}).", resp.StatusCode);
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public static string ImageUrl(string imageId, string size) => $"{ImageBase}t_{size}/{imageId}.jpg";

    // ------------------------------------------------------------- "URL or ID" input
    [GeneratedRegex(@"igdb\.com/games/([a-z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GameUrlRe();

    /// <summary>Parses what the user pasted: a numeric IGDB ID, an igdb.com/games/… URL, or a bare slug.</summary>
    public static (long? Id, string? Slug) ParseReference(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return (null, null);
        if (long.TryParse(t, out var id) && id > 0) return (id, null);
        if (GameUrlRe().Match(t) is { Success: true } m) return (null, m.Groups[1].Value.ToLowerInvariant());
        if (Regex.IsMatch(t, @"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.IgnoreCase)) return (null, t.ToLowerInvariant());
        return (null, null);
    }

    public async Task<IgdbGame?> GetByReferenceAsync(string text, CancellationToken ct = default) =>
        ParseReference(text) switch
        {
            ({ } id, _) => await GetByIdAsync(id, ct),
            (_, { } slug) => await GetBySlugAsync(slug, ct),
            _ => null,
        };
}
