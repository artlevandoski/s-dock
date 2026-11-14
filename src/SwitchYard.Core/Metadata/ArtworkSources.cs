namespace SwitchYard.Core.Metadata;

/// <summary>One image the user can pick in Choose Background… or Choose Logo….</summary>
public sealed record ArtCandidate(
    string Source,
    int Width,
    int Height,
    string Extension,
    Func<CancellationToken, Task<byte[]>> FetchThumb,
    Func<CancellationToken, Task<byte[]>> FetchFull)
{
    public string Detail => Width > 0 ? $"{Source}  ·  {Width}×{Height}" : Source;
}

/// <summary>
/// Gathers background (key art) and clear-logo choices from IGDB and SteamGridDB, fetches a logo
/// automatically, and saves a chosen image as the user's own art (kept on refresh).
/// Either client may be null when its credentials aren't set.
/// </summary>
public sealed class ArtworkSources(LibraryStore library, IgdbClient? igdb, SteamGridDbClient? sgdb, string artworkDir)
{
    public const string IgdbBackgroundThumb = "screenshot_med"; // 569×320

    public bool HasSteamGridDb => sgdb is not null;

    /// <summary>The SteamGridDB game for an edition (remembered once found), matched by its IGDB name or title.</summary>
    private async Task<long?> SgdbGameIdAsync(Edition edition, CancellationToken ct)
    {
        if (sgdb is null) return null;
        var m = library.GetMetadata(edition.Id);
        if (m.SteamGridDbId is { } known) return known;
        var found = await sgdb.FindGameAsync(m.IgdbName ?? edition.Title, ct);
        if (found is null && m.IgdbName is not null && m.IgdbName != edition.Title)
            found = await sgdb.FindGameAsync(edition.Title, ct);
        if (found is null) return null;
        library.SaveMetadata(library.GetMetadata(edition.Id) with { SteamGridDbId = found.Id });
        return found.Id;
    }

    private ArtCandidate FromSgdb(SgdbImage img, string what) => new(
        $"SteamGridDB {what}" + (img.Style is { Length: > 0 } s ? $" ({s})" : ""),
        img.Width, img.Height,
        img.Mime.Contains("png") || img.Url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : img.Url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".jpg",
        ct => sgdb!.DownloadAsync(img.Thumb.Length > 0 ? img.Thumb : img.Url, ct),
        ct => sgdb!.DownloadAsync(img.Url, ct));

    /// <summary>Backgrounds: IGDB artworks and screenshots of the matched game, plus SteamGridDB heroes.</summary>
    public async Task<List<ArtCandidate>> BackgroundCandidatesAsync(Edition edition, CancellationToken ct = default)
    {
        var list = new List<ArtCandidate>();
        if (igdb is not null && edition.IgdbId is { } igdbId && await igdb.GetByIdAsync(igdbId, ct) is { } game)
        {
            foreach (var a in game.Artworks ?? [])
                list.Add(Igdb(a.ImageId, "IGDB artwork"));
            foreach (var s in game.Screenshots ?? [])
                list.Add(Igdb(s.ImageId, "IGDB screenshot"));
        }
        if (await SgdbGameIdAsync(edition, ct) is { } sgdbId)
            list.AddRange((await sgdb!.HeroesAsync(sgdbId, ct)).Select(h => FromSgdb(h, "hero")));
        return list;

        ArtCandidate Igdb(string imageId, string source) => new(source, 1920, 1080, ".jpg",
            ct2 => igdb!.DownloadImageAsync(imageId, IgdbBackgroundThumb, ct2),
            ct2 => igdb!.DownloadImageAsync(imageId, MetadataService.KeyArtSize, ct2));
    }

    public const string IgdbLogoThumb = "logo_med"; // fits 284×160
    public const string IgdbLogoFull = "1080p";     // fits 1920×1080

    /// <summary>True when at least one logo source is available.</summary>
    public bool CanFetchLogos => igdb is not null || sgdb is not null;

    /// <summary>The matched game's IGDB logos (as PNG, keeping transparency), best first.</summary>
    private async Task<List<ArtCandidate>> IgdbLogoCandidatesAsync(Edition edition, CancellationToken ct)
    {
        if (igdb is null || edition.IgdbId is not { } igdbId) return [];
        List<IgdbLogo> logos;
        try
        {
            logos = await igdb.LogosAsync(igdbId, ct);
        }
        catch (IgdbException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            return []; // an IGDB account or API version without the logos endpoint: fall back to SteamGridDB
        }
        return logos.Select(l => new ArtCandidate(
            "IGDB logo" + (l.ImageType?.Name is { Length: > 0 } t ? $" ({t})" : l.AlphaChannel ? " (transparent)" : ""),
            l.Width, l.Height, ".png",
            ct2 => igdb.DownloadPngAsync(l.ImageId, IgdbLogoThumb, ct2),
            ct2 => igdb.DownloadPngAsync(l.ImageId, IgdbLogoFull, ct2))).ToList();
    }

    /// <summary>Logos from IGDB first (no extra key needed), then SteamGridDB, best first within each.</summary>
    public async Task<List<ArtCandidate>> LogoCandidatesAsync(Edition edition, CancellationToken ct = default)
    {
        var list = await IgdbLogoCandidatesAsync(edition, ct);
        if (await SgdbGameIdAsync(edition, ct) is { } id)
            list.AddRange((await sgdb!.LogosAsync(id, ct)).Select(l => FromSgdb(l, "logo")));
        return list;
    }

    /// <summary>
    /// Downloads the best logo, from IGDB if it has one for the matched game, otherwise from
    /// SteamGridDB (official style first), unless one is already cached. Returns true when a logo was
    /// saved. The user's own logo is never touched.
    /// </summary>
    public async Task<bool> FetchLogoAsync(Edition edition, bool replace = false, CancellationToken ct = default)
    {
        if (!CanFetchLogos) return false;
        var m = library.GetMetadata(edition.Id);
        if (!replace && m.LogoFile is { } existing && File.Exists(Path.Combine(artworkDir, existing))) return false;
        var best = (await IgdbLogoCandidatesAsync(edition, ct)).FirstOrDefault();
        if (best is null && await SgdbGameIdAsync(edition, ct) is { } id &&
            (await sgdb!.LogosAsync(id, ct)).FirstOrDefault() is { } sgdbLogo)
            best = FromSgdb(sgdbLogo, "logo");
        if (best is null) return false;
        var bytes = await best.FetchFull(ct);
        var dir = Path.Combine(artworkDir, edition.Id);
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "logo.*").Where(f => !f.EndsWith(".download")))
            File.Delete(old); // a previous logo may have had another extension
        var target = Path.Combine(dir, "logo" + best.Extension);
        await ArtFiles.WriteAsync(target, bytes, ct);
        library.SaveMetadata(library.GetMetadata(edition.Id) with { LogoFile = Path.GetRelativePath(artworkDir, target) });
        return true;
    }

    /// <summary>Saves the chosen image as the user's own cover, background or logo.</summary>
    public async Task UseAsync(string editionId, ArtKind kind, ArtCandidate candidate, CancellationToken ct = default)
    {
        var bytes = await candidate.FetchFull(ct);
        UserMetadata.SetArtBytes(library, artworkDir, editionId, kind, bytes, candidate.Extension);
    }
}
