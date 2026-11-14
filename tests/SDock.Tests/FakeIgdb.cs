using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>
/// Stand-in for Twitch sign-in, the IGDB API and the IGDB image server. Games are searched by
/// loose title match and filtered by the requested platforms, like the real service.
/// </summary>
public sealed class FakeIgdb : HttpMessageHandler
{
    public const long Switch1Platform = 130;
    public const long Switch2Platform = 508;

    /// <summary>A valid 1×1 PNG, used for every image unless <see cref="ImageBytes"/> is set.</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public List<IgdbGame> Games { get; } = [];

    /// <summary>Logos by IGDB game ID, served from the <c>logos</c> endpoint.</summary>
    public Dictionary<long, List<IgdbLogo>> Logos { get; } = [];

    /// <summary>Simulate an API without the logos endpoint (404).</summary>
    public bool NoLogosEndpoint { get; set; }

    public static IgdbLogo Logo(long id, bool transparent = true, string? type = null, int width = 800, int height = 300) => new()
    {
        Id = id,
        ImageId = $"lo{id}",
        Width = width,
        Height = height,
        AlphaChannel = transparent,
        ImageType = type is null ? null : new IgdbNamed { Id = 1, Name = type },
    };
    public bool RejectCredentials { get; set; }
    public int UnauthorizedResponsesLeft { get; set; }
    public Func<string, byte[]>? ImageBytes { get; set; }

    public int TokenRequests { get; private set; }
    public List<(string Endpoint, string Body, string? ClientId, string? Bearer)> Queries { get; } = [];
    public List<string> ImageRequests { get; } = [];

    public static IgdbNamed Switch1 => new() { Id = Switch1Platform, Name = "Nintendo Switch", Abbreviation = "Switch" };
    public static IgdbNamed Switch2 => new() { Id = Switch2Platform, Name = "Nintendo Switch 2", Abbreviation = "NS2" };
    public static IgdbNamed Pc => new() { Id = 6, Name = "PC (Microsoft Windows)", Abbreviation = "PC" };

    public static IgdbGame Game(long id, string name, params IgdbNamed[] platforms) => new()
    {
        Id = id,
        Name = name,
        Slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'),
        Url = $"https://www.igdb.com/games/{id}",
        Summary = $"{name} summary from IGDB.",
        FirstReleaseDate = 1672531200, // 2023-01-01
        Genres = [new() { Id = 8, Name = "Platform" }, new() { Id = 31, Name = "Adventure" }],
        Platforms = [.. platforms],
        Cover = new() { ImageId = $"co{id}" },
        Artworks = [new() { ImageId = $"ar{id}" }],
        Screenshots = [new() { ImageId = $"sc{id}a" }, new() { ImageId = $"sc{id}b" }],
    };

    public IgdbClient Client() => new("test-client-id", "test-secret", this) { Throttle = false };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

        if (url.StartsWith("https://id.twitch.tv/oauth2/token"))
        {
            TokenRequests++;
            return RejectCredentials
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"message\":\"invalid client secret\"}") }
                : Json($"{{\"access_token\":\"token-{TokenRequests}\",\"expires_in\":5000000,\"token_type\":\"bearer\"}}");
        }

        if (url.StartsWith(IgdbClient.ImageBase))
        {
            ImageRequests.Add(url);
            var bytes = ImageBytes?.Invoke(url) ?? TinyPng;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }

        if (url.StartsWith(IgdbClient.ApiBase))
        {
            var endpoint = url[IgdbClient.ApiBase.Length..];
            request.Headers.TryGetValues("Client-ID", out var ids);
            Queries.Add((endpoint, body, ids?.FirstOrDefault(), request.Headers.Authorization?.Parameter));
            if (UnauthorizedResponsesLeft > 0)
            {
                UnauthorizedResponsesLeft--;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return endpoint switch
            {
                "platforms" => Json($"[{{\"id\":{Switch1Platform},\"name\":\"Nintendo Switch\"}},{{\"id\":{Switch2Platform},\"name\":\"Nintendo Switch 2\"}}]"),
                "games" => Json(JsonSerializer.Serialize(Answer(body))),
                "logos" when NoLogosEndpoint => new HttpResponseMessage(HttpStatusCode.NotFound),
                "logos" => Json(JsonSerializer.Serialize(
                    Logos.GetValueOrDefault(long.Parse(Regex.Match(body, @"where game = (\d+)").Groups[1].Value), []))),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private IEnumerable<IgdbGame> Answer(string body)
    {
        if (Regex.Match(body, @"where id = (\d+)") is { Success: true } byId)
            return Games.Where(g => g.Id == long.Parse(byId.Groups[1].Value)).Take(1);
        if (Regex.Match(body, "where slug = \"([^\"]+)\"") is { Success: true } bySlug)
            return Games.Where(g => g.Slug == bySlug.Groups[1].Value).Take(1);

        var query = Regex.Match(body, "search \"([^\"]*)\"").Groups[1].Value;
        var words = MetadataService.MatchKey(query);
        IEnumerable<IgdbGame> results = Games.Where(g =>
            MetadataService.MatchKey(g.Name).Contains(words) || words.Contains(MetadataService.MatchKey(g.Name)));
        if (Regex.Match(body, @"where platforms = \(([\d,]+)\)") is { Success: true } byPlatform)
        {
            var wanted = byPlatform.Groups[1].Value.Split(',').Select(long.Parse).ToHashSet();
            results = results.Where(g => g.Platforms?.Any(p => wanted.Contains(p.Id)) ?? false);
        }
        return results;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
