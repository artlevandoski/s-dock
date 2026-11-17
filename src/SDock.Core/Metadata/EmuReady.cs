using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SDock.Core.Metadata;

/// <summary>One emulator's rating for a game: the typical (median) rating across its reports.</summary>
public sealed record EmuReadyRating(string Emulator, string Rating, int Rank, int Reports);

/// <summary>
/// What EmuReady says about one game. GameId null means "looked up, not on EmuReady" (kept so it isn't asked
/// again every time). Emulators are ordered with the most-reported first. Source is "PC", or "handheld" when
/// there are no PC reports and the ratings come from handheld devices instead.
/// </summary>
public sealed record EmuReadySummary(string? GameId, string? GameTitle, IReadOnlyList<EmuReadyRating> Emulators, DateTime FetchedUtc,
    string Source = EmuReadySummary.Pc)
{
    public const string Pc = "PC";
    public const string Handheld = "handheld";

    public int Reports => Emulators.Sum(e => e.Reports);
    public bool Found => GameId is not null;
    public string? Url => GameId is null ? null : EmuReadyClient.GameUrl(GameId);

    public static EmuReadySummary NotFound(DateTime fetchedUtc) => new(null, null, [], fetchedUtc);

    /// <summary>The rating to headline: for this emulator (by name, e.g. "eden") when it has reports, else the most reported.</summary>
    public EmuReadyRating? For(string? emulator) =>
        (emulator is { Length: > 0 } ? Emulators.FirstOrDefault(e => EmuReadyClient.SameEmulator(e.Emulator, emulator)) : null)
        ?? Emulators.FirstOrDefault();

    public string ToJson() => JsonSerializer.Serialize(this);
    public static EmuReadySummary? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<EmuReadySummary>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>A single PC report: which emulator, how well it ran, and on which OS.</summary>
public sealed record EmuReadyReport(string Emulator, string Rating, int Rank, string Os);

public sealed record EmuReadyGame(string Id, string Title, string SystemKey, int Listings);

/// <summary>
/// EmuReady (emuready.com): community reports of how games run on emulators. Read-only use of its public
/// API (no key): find the game by name for its console, then summarise its PC reports per emulator.
/// </summary>
public sealed class EmuReadyClient : IDisposable
{
    public const string BaseUrl = "https://www.emuready.com/api/mobile/trpc/";
    public const string SiteUrl = "https://www.emuready.com/";
    public const string Switch1Key = "nintendo_switch";
    public const string Switch2Key = "nintendo_switch_2";

    private readonly HttpClient _http;

    public EmuReadyClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("S-Dock/1.0");
    }

    public void Dispose() => _http.Dispose();

    public static string GameUrl(string gameId) => SiteUrl + "games/" + Uri.EscapeDataString(gameId);

    /// <summary>Looks the game up and summarises its PC reports. Null game id in the result: not on EmuReady.</summary>
    public async Task<EmuReadySummary> LookUpAsync(string title, string generation, CancellationToken ct = default)
    {
        var game = await FindGameAsync(title, generation, ct).ConfigureAwait(false);
        if (game is null) return EmuReadySummary.NotFound(DateTime.UtcNow);
        var reports = await PcReportsAsync(game.Id, ct).ConfigureAwait(false);
        if (reports.Count > 0) return new EmuReadySummary(game.Id, game.Title, Summarize(reports), DateTime.UtcNow);
        // Most reports come from handhelds: better than nothing when no one has reported on a PC yet.
        var handheld = await HandheldReportsAsync(game.Id, ct).ConfigureAwait(false);
        return new EmuReadySummary(game.Id, game.Title, Summarize(handheld), DateTime.UtcNow, EmuReadySummary.Handheld);
    }

    /// <summary>The EmuReady game for this title and console, or null when there's no confident match.</summary>
    public async Task<EmuReadyGame?> FindGameAsync(string title, string generation, CancellationToken ct = default)
    {
        var search = SearchText(title);
        if (search.Length == 0) return null;
        using var doc = await GetAsync("games.get", new { search, limit = 20 }, ct).ConfigureAwait(false);
        var games = new List<EmuReadyGame>();
        if (Data(doc) is { } data && data.TryGetProperty("games", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var g in list.EnumerateArray())
                games.Add(new EmuReadyGame(
                    Str(g, "id") ?? "",
                    Str(g, "title") ?? "",
                    g.TryGetProperty("system", out var sys) ? Str(sys, "key") ?? "" : "",
                    g.TryGetProperty("_count", out var count) && count.TryGetProperty("listings", out var n) && n.TryGetInt32(out var v) ? v : 0));
        return BestMatch(title, generation, games);
    }

    /// <summary>The game's PC reports (approved ones; the API returns only those).</summary>
    public Task<List<EmuReadyReport>> PcReportsAsync(string gameId, CancellationToken ct = default) =>
        ReportsAsync("pcListings.get", gameId, 50, ct);

    /// <summary>The game's reports from handheld devices (Android handhelds and phones).</summary>
    public Task<List<EmuReadyReport>> HandheldReportsAsync(string gameId, CancellationToken ct = default) =>
        ReportsAsync("listings.byGame", gameId, 100, ct); // (listings.get ignores gameId)

    private async Task<List<EmuReadyReport>> ReportsAsync(string procedure, string gameId, int limit, CancellationToken ct)
    {
        var reports = new List<EmuReadyReport>();
        for (var page = 1; page <= 3; page++) // a few hundred reports is plenty for a typical rating
        {
            using var doc = await GetAsync(procedure, new { gameId, page, limit }, ct).ConfigureAwait(false);
            if (Data(doc) is not { } data || !data.TryGetProperty("listings", out var list) || list.ValueKind != JsonValueKind.Array) break;
            var others = 0;
            foreach (var l in list.EnumerateArray())
            {
                if (Str(l, "gameId") is { } id && id != gameId) { others++; continue; } // only this game, whatever the API does
                var emulator = l.TryGetProperty("emulator", out var e) ? Str(e, "name") : null;
                var perf = l.TryGetProperty("performance", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                var rating = perf.ValueKind == JsonValueKind.Object ? Str(perf, "label") : null;
                var rank = perf.ValueKind == JsonValueKind.Object && perf.TryGetProperty("rank", out var r) && r.TryGetInt32(out var rv) ? rv : 0;
                if (emulator is null || rating is null || rank <= 0) continue;
                reports.Add(new EmuReadyReport(emulator, rating, rank, Str(l, "os") ?? ""));
            }
            var more = data.TryGetProperty("pagination", out var pg) && pg.TryGetProperty("hasNextPage", out var next) && next.ValueKind == JsonValueKind.True;
            if (!more || others > 0) break; // other games mixed in: the filter wasn't applied, so don't page through everything
        }
        return reports;
    }

    /// <summary>
    /// Per emulator: the median rating of its reports (Windows reports when there are any, since S-Dock runs on
    /// Windows), most-reported emulator first.
    /// </summary>
    public static IReadOnlyList<EmuReadyRating> Summarize(IEnumerable<EmuReadyReport> reports)
    {
        var all = reports.ToList();
        var windows = all.Where(r => r.Os.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase)).ToList();
        var use = windows.Count > 0 ? windows : all;
        return use.GroupBy(r => r.Emulator, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sorted = g.OrderBy(r => r.Rank).ToList();
                var median = sorted[(sorted.Count - 1) / 2]; // the better of the two middles
                return new EmuReadyRating(g.First().Emulator, median.Rating, median.Rank, sorted.Count);
            })
            .OrderByDescending(r => r.Reports).ThenBy(r => r.Rank).ThenBy(r => r.Emulator, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Exact title (ignoring case, ™, punctuation) on the right console; then one title starting with the other.</summary>
    public static EmuReadyGame? BestMatch(string title, string generation, IReadOnlyList<EmuReadyGame> games)
    {
        var keys = generation switch
        {
            Generations.Switch1 => new[] { Switch1Key },
            Generations.Switch2 => new[] { Switch2Key, Switch1Key },
            _ => new[] { Switch1Key, Switch2Key },
        };
        var wanted = Normalize(title);
        if (wanted.Length == 0) return null;
        foreach (var key in keys)
        {
            var onConsole = games.Where(g => g.SystemKey == key && g.Id.Length > 0).ToList();
            var exact = onConsole.Where(g => Normalize(g.Title) == wanted).OrderByDescending(g => g.Listings).FirstOrDefault();
            if (exact is not null) return exact;
            var close = onConsole.Where(g =>
            {
                var t = Normalize(g.Title);
                return t.Length > 0 && (t.StartsWith(wanted + " ", StringComparison.Ordinal) || wanted.StartsWith(t + " ", StringComparison.Ordinal));
            }).OrderByDescending(g => g.Listings).ToList();
            if (close.Count == 1) return close[0];
        }
        return null;
    }

    /// <summary>"Super Mario Bros.™ Wonder" → "super mario bros wonder".</summary>
    public static string Normalize(string title)
    {
        var s = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // é → e
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
            else if (c is '\'' or '’' or '.' or '™' or '®' or '©') { }
            else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
        }
        return sb.ToString().Trim();
    }

    /// <summary>What to type into EmuReady's search: the title without ™ and ® and edition notes in brackets.</summary>
    public static string SearchText(string title)
    {
        var t = title.Replace("™", "").Replace("®", "").Replace("©", "").Replace('’', '\'').Replace('‘', '\'');
        var cut = t.IndexOfAny(['(', '[']);
        if (cut > 0) t = t[..cut];
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Eden matches "Eden"; "ryujinx" matches "Ryujinx/Ryubing".</summary>
    public static bool SameEmulator(string emuReadyName, string emulator)
    {
        var a = Normalize(emuReadyName);
        var b = Normalize(emulator);
        return b.Length > 0 && (a == b || a.Split(' ').Contains(b));
    }

    private async Task<JsonDocument> GetAsync(string procedure, object input, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { json = input });
        var url = BaseUrl + procedure + "?input=" + Uri.EscapeDataString(json);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static JsonElement? Data(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("data", out var d) && d.TryGetProperty("json", out var j)
            ? j : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
