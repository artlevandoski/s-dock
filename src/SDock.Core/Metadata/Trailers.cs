namespace SDock.Core.Metadata
{

/// <summary>A game's trailer as S-Dock knows it: a YouTube video ID, or null when IGDB had none.</summary>
public sealed record TrailerInfo(string? VideoId, DateTime FetchedUtc);

public sealed partial class MetadataService
{
    /// <summary>
    /// The game's trailer from IGDB: from its IGDB match, or else (without changing its match) the one IGDB game that is
    /// clearly it. Saves what it finds, including "none", and returns the YouTube video ID or null.
    /// </summary>
    public async Task<string?> FindTrailerAsync(Edition edition, CancellationToken ct = default)
    {
        IgdbGame? game;
        if (edition.IgdbId is { } id)
            game = await Client.GetByIdAsync(id, ct);
        else
        {
            var platforms = await PlatformsForAsync(edition.Generation, ct);
            game = PickConfident(edition.Title, platforms, await Client.SearchAsync(edition.Title, platforms, ct: ct));
        }
        var video = game?.TrailerVideoId;
        library.SetTrailer(edition.Id, video);
        return video;
    }
}
}

namespace SDock.Core
{
    using SDock.Core.Metadata;

public sealed partial class LibraryStore
{
    public void SetTrailer(string editionId, string? videoId) =>
        Exec("""
             INSERT INTO trailers(edition_id, video_id, fetched_at) VALUES ($e, $v, $t)
             ON CONFLICT(edition_id) DO UPDATE SET video_id = excluded.video_id, fetched_at = excluded.fetched_at
             """, ("$e", editionId), ("$v", videoId), ("$t", UtcNow()));

    /// <summary>Forgets a game's trailer, so it's looked up again.</summary>
    public void ForgetTrailer(string editionId) => Exec("DELETE FROM trailers WHERE edition_id = $e", ("$e", editionId));

    public TrailerInfo? GetTrailer(string editionId)
    {
        using var cmd = Cmd("SELECT video_id, fetched_at FROM trailers WHERE edition_id = $e", ("$e", editionId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var when = DateTime.TryParse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.MinValue;
        return new TrailerInfo(r.IsDBNull(0) ? null : r.GetString(0), when);
    }
}
}
