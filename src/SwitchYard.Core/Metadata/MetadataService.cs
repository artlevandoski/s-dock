using System.Text.RegularExpressions;

namespace SwitchYard.Core.Metadata;

public enum MatchOutcome
{
    /// <summary>Matched confidently and metadata saved.</summary>
    Matched,
    /// <summary>IGDB had candidates but none was a clear match: the user picks with Match on IGDB….</summary>
    NeedsMatch,
    /// <summary>IGDB had nothing for this title on these platforms.</summary>
    NotFound,
    /// <summary>Already matched; left alone.</summary>
    Skipped,
}

public sealed record MatchAllSummary(int Matched, int NeedsMatch, int NotFound, int Failed, bool Cancelled);

/// <summary>
/// Matches editions to IGDB, saves metadata, and caches artwork in the data folder.
/// User-edited descriptions and artwork are kept apart and never overwritten.
/// </summary>
public sealed partial class MetadataService(LibraryStore library, IgdbClient client, string artworkDir)
{
    public const string CoverSize = "cover_big_2x";  // 528×748
    public const string KeyArtSize = "1080p";        // 1920×1080
    public const string ThumbSize = "cover_small";   // 90×128, for search results

    public IgdbClient Client { get; } = client;

    [GeneratedRegex(@"[™®©]")]
    private static partial Regex TrademarkRe();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlnumRe();

    /// <summary>Loose title key: case, punctuation, ™/® and a leading "The" don't matter.</summary>
    public static string MatchKey(string title)
    {
        var t = TrademarkRe().Replace(title, "").ToLowerInvariant().Replace("&", " and ").Trim();
        if (t.StartsWith("the ", StringComparison.Ordinal)) t = t[4..];
        return NonAlnumRe().Replace(t, "");
    }

    /// <summary>IGDB platforms to search for a generation: Switch 1, Switch 2, or both when unclassified.</summary>
    public async Task<List<long>> PlatformsForAsync(string generation, CancellationToken ct = default)
    {
        var map = await Client.SwitchPlatformsAsync(ct);
        return generation switch
        {
            Generations.Switch1 or Generations.Switch2 => map.TryGetValue(generation, out var id) ? [id] : [.. map.Values],
            _ => [.. map.Values],
        };
    }

    public async Task<List<IgdbGame>> SearchAsync(string query, string generation, bool anyPlatform, CancellationToken ct = default) =>
        await Client.SearchAsync(query, anyPlatform ? [] : await PlatformsForAsync(generation, ct), ct: ct);

    /// <summary>
    /// The one result that is clearly this game: same title (loosely compared) and, if several share
    /// the title, the only one on the game's platform. Anything less certain is left for the user.
    /// </summary>
    public static IgdbGame? PickConfident(string title, IReadOnlyList<long> platforms, IReadOnlyList<IgdbGame> results)
    {
        var key = MatchKey(title);
        var exact = results.Where(g => MatchKey(g.Name) == key).ToList();
        if (exact.Count == 1) return exact[0];
        var onPlatform = exact.Where(g => g.Platforms?.Any(p => platforms.Contains(p.Id)) ?? false).ToList();
        return onPlatform.Count == 1 ? onPlatform[0] : null;
    }

    public async Task<MatchOutcome> AutoMatchAsync(Edition edition, CancellationToken ct = default)
    {
        if (edition.IgdbId is not null) return MatchOutcome.Skipped;
        var platforms = await PlatformsForAsync(edition.Generation, ct);
        var results = await Client.SearchAsync(edition.Title, platforms, ct: ct);
        if (results.Count == 0) return MatchOutcome.NotFound;
        if (PickConfident(edition.Title, platforms, results) is not { } game) return MatchOutcome.NeedsMatch;
        await ApplyMatchAsync(edition.Id, game, ct);
        return MatchOutcome.Matched;
    }

    /// <summary>Matches every unmatched edition. Confident matches are applied; the rest are left for review.</summary>
    public async Task<MatchAllSummary> MatchAllAsync(IReadOnlyList<Edition> editions,
        IProgress<(int Done, int Total, string Title)>? progress = null, CancellationToken ct = default)
    {
        int matched = 0, needs = 0, notFound = 0, failed = 0, done = 0;
        var todo = editions.Where(e => e.IgdbId is null).ToList();
        foreach (var e in todo)
        {
            if (ct.IsCancellationRequested)
                return new MatchAllSummary(matched, needs, notFound, failed, Cancelled: true);
            progress?.Report((done, todo.Count, e.Title));
            try
            {
                switch (await AutoMatchAsync(e, ct))
                {
                    case MatchOutcome.Matched: matched++; break;
                    case MatchOutcome.NeedsMatch: needs++; break;
                    case MatchOutcome.NotFound: notFound++; break;
                }
            }
            catch (OperationCanceledException)
            {
                return new MatchAllSummary(matched, needs, notFound, failed, Cancelled: true);
            }
            catch (IgdbException ex) when (ex.Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest
                                               or System.Net.HttpStatusCode.Forbidden)
            {
                throw; // bad credentials: stop rather than fail every game
            }
            catch (Exception ex) when (ex is IgdbException or HttpRequestException or IOException)
            {
                failed++;
            }
            done++;
        }
        progress?.Report((done, todo.Count, ""));
        return new MatchAllSummary(matched, needs, notFound, failed, Cancelled: false);
    }

    /// <summary>Saves an IGDB match: IGDB ID, text and cached artwork. The user's own edits are kept.</summary>
    public async Task ApplyMatchAsync(string editionId, IgdbGame game, CancellationToken ct = default)
    {
        var dir = Path.Combine(artworkDir, editionId);
        Directory.CreateDirectory(dir);
        var cover = game.Cover?.ImageId is { Length: > 0 } c ? await SaveImageAsync(dir, "cover.jpg", c, CoverSize, ct) : null;
        var keyArt = game.KeyArtImageId is { Length: > 0 } k ? await SaveImageAsync(dir, "keyart.jpg", k, KeyArtSize, ct) : null;
        if (cover is null) TryDelete(Path.Combine(dir, "cover.jpg"));
        if (keyArt is null) TryDelete(Path.Combine(dir, "keyart.jpg"));

        var existing = library.GetMetadata(editionId);
        if (library.GetEdition(editionId)?.IgdbId is { } previous && previous != game.Id)
        {
            // A different game: its SteamGridDB link and logo belonged to the old match.
            if (existing.LogoFile is not null) TryDelete(Path.Combine(artworkDir, existing.LogoFile));
            existing = existing with { LogoFile = null, SteamGridDbId = null };
        }
        library.SaveMetadata(existing with
        {
            IgdbName = game.Name,
            IgdbSlug = game.Slug,
            IgdbUrl = game.Url,
            Summary = game.Summary,
            Genres = game.GenreNames.ToList(),
            ReleaseYear = game.ReleaseYear,
            CoverFile = cover,
            KeyArtFile = keyArt,
            FetchedAt = LibraryStore.UtcNow(),
        });
        library.SetIgdbId(editionId, game.Id);
    }

    /// <summary>Fetches the matched game again (new description, art). User edits are kept.</summary>
    public async Task<bool> RefreshAsync(Edition edition, CancellationToken ct = default)
    {
        if (edition.IgdbId is not { } id) return false;
        if (await Client.GetByIdAsync(id, ct) is not { } game) return false;
        await ApplyMatchAsync(edition.Id, game, ct);
        return true;
    }

    /// <summary>Forgets the IGDB match and its cached artwork. The user's own text and art stay.</summary>
    public static void RemoveMatch(LibraryStore library, string artworkDir, string editionId)
    {
        var m = library.GetMetadata(editionId);
        foreach (var f in new[] { m.CoverFile, m.KeyArtFile })
            if (f is not null) TryDelete(Path.Combine(artworkDir, f));
        if (m.LogoFile is not null) TryDelete(Path.Combine(artworkDir, m.LogoFile));
        library.SaveMetadata(m with
        {
            IgdbName = null, IgdbSlug = null, IgdbUrl = null, Summary = null, Genres = [],
            ReleaseYear = null, CoverFile = null, KeyArtFile = null, FetchedAt = null,
            LogoFile = null, SteamGridDbId = null,
        });
        library.SetIgdbId(editionId, null);
    }

    private async Task<string> SaveImageAsync(string dir, string name, string imageId, string size, CancellationToken ct)
    {
        var bytes = await Client.DownloadImageAsync(imageId, size, ct);
        var target = Path.Combine(dir, name);
        var temp = target + ".download";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, target, overwrite: true);
        return Path.GetRelativePath(artworkDir, target);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>User edits that don't need IGDB: own description and artwork.</summary>
public static class UserMetadata
{
    private static string UserFileName(ArtKind kind) => kind switch
    {
        ArtKind.Cover => "user-cover",
        ArtKind.KeyArt => "user-keyart",
        _ => "user-logo",
    };

    /// <summary>Copies an image the user chose into the artwork folder and uses it for this game.</summary>
    public static void SetArt(LibraryStore library, string artworkDir, string editionId, ArtKind kind, string sourceFile)
    {
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            throw new InvalidOperationException("Choose a PNG, JPEG, WebP or BMP image.");
        SetArtBytes(library, artworkDir, editionId, kind, File.ReadAllBytes(sourceFile), ext);
    }

    /// <summary>Stores image bytes (picked in a chooser, or copied from a file) as the user's own art.</summary>
    public static void SetArtBytes(LibraryStore library, string artworkDir, string editionId, ArtKind kind, byte[] bytes, string ext)
    {
        var dir = Path.Combine(artworkDir, editionId);
        Directory.CreateDirectory(dir);
        var m = library.GetMetadata(editionId);
        var previous = kind switch { ArtKind.Cover => m.UserCoverFile, ArtKind.KeyArt => m.UserKeyArtFile, _ => m.UserLogoFile };
        if (previous is not null)
            try { File.Delete(Path.Combine(artworkDir, previous)); } catch (IOException) { }
        var target = Path.Combine(dir, UserFileName(kind) + ext.ToLowerInvariant());
        File.WriteAllBytes(target, bytes);
        var rel = Path.GetRelativePath(artworkDir, target);
        library.SaveMetadata(kind switch
        {
            ArtKind.Cover => m with { UserCoverFile = rel },
            ArtKind.KeyArt => m with { UserKeyArtFile = rel },
            _ => m with { UserLogoFile = rel },
        });
    }

    /// <summary>Drops the user's image so IGDB's (or the placeholder) shows again.</summary>
    public static void ResetArt(LibraryStore library, string artworkDir, string editionId, ArtKind kind)
    {
        var m = library.GetMetadata(editionId);
        var file = kind switch { ArtKind.Cover => m.UserCoverFile, ArtKind.KeyArt => m.UserKeyArtFile, _ => m.UserLogoFile };
        if (file is not null)
            try { File.Delete(Path.Combine(artworkDir, file)); } catch (IOException) { }
        library.SaveMetadata(kind switch
        {
            ArtKind.Cover => m with { UserCoverFile = null },
            ArtKind.KeyArt => m with { UserKeyArtFile = null },
            _ => m with { UserLogoFile = null },
        });
    }

    /// <summary>Sets the user's own description; null or blank goes back to IGDB's.</summary>
    public static void SetSummary(LibraryStore library, string editionId, string? text) =>
        library.SaveMetadata(library.GetMetadata(editionId) with { UserSummary = string.IsNullOrWhiteSpace(text) ? null : text.Trim() });
}
