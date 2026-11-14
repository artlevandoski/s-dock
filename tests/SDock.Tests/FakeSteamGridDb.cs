using System.Net;
using System.Text;
using System.Text.Json;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>Stand-in for the SteamGridDB API and its image CDN.</summary>
public sealed class FakeSteamGridDb : HttpMessageHandler
{
    public List<SgdbGame> Games { get; } = [];
    public Dictionary<long, List<SgdbImage>> Logos { get; } = [];
    public Dictionary<long, List<SgdbImage>> Heroes { get; } = [];
    public Dictionary<long, List<SgdbImage>> Grids { get; } = [];
    public bool RejectKey { get; set; }
    public Func<string, byte[]>? ImageBytes { get; set; }

    public List<string> Requests { get; } = [];
    public List<string?> AuthHeaders { get; } = [];

    public SteamGridDbClient Client() => new("test-sgdb-key", this);

    public static SgdbImage Image(long id, string kind, string style = "official", int score = 0, string language = "en") => new()
    {
        Id = id,
        Style = style,
        Score = score,
        Language = language,
        Width = kind == "logo" ? 1000 : 1920,
        Height = kind == "logo" ? 400 : 620,
        Mime = "image/png",
        Url = $"https://cdn2.steamgriddb.com/{kind}/{id}.png",
        Thumb = $"https://cdn2.steamgriddb.com/{kind}_thumb/{id}.png",
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        AuthHeaders.Add(request.Headers.Authorization?.ToString());

        // Like the real image server: a request that carries the API key is refused.
        if (url.StartsWith("https://cdn2.steamgriddb.com/") && request.Headers.Authorization is not null)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        if (url.StartsWith("https://cdn2.steamgriddb.com/"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ImageBytes?.Invoke(url) ?? FakeIgdb.TinyPng),
            });
        if (!url.StartsWith(SteamGridDbClient.ApiBase))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if (RejectKey)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var path = url[SteamGridDbClient.ApiBase.Length..];
        object data;
        if (path.StartsWith("search/autocomplete/"))
        {
            var term = Uri.UnescapeDataString(path["search/autocomplete/".Length..]);
            var key = MetadataService.MatchKey(term);
            data = Games.Where(g => MetadataService.MatchKey(g.Name).Contains(key)).ToList();
        }
        else if (path.StartsWith("logos/game/"))
            data = Logos.GetValueOrDefault(long.Parse(path["logos/game/".Length..].Split('?')[0]), []);
        else if (path.StartsWith("grids/game/"))
        {
            var all = Grids.GetValueOrDefault(long.Parse(path["grids/game/".Length..].Split('?')[0]), []);
            var square = path.Contains("512x512");
            data = all.Where(g => (g.Width == g.Height) == square).ToList();
        }
        else if (path.StartsWith("heroes/game/"))
            data = Heroes.GetValueOrDefault(long.Parse(path["heroes/game/".Length..].Split('?')[0]), []);
        else
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var json = JsonSerializer.Serialize(new { success = true, data });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }
}
