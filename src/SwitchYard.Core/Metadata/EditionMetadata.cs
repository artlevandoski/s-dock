namespace SwitchYard.Core.Metadata;

/// <summary>
/// Descriptive data for an edition. IGDB values and the user's own edits are kept in separate
/// fields: a refresh from IGDB replaces only the IGDB fields, and what is shown prefers the user's.
/// Artwork file names are relative to <see cref="AppPaths.ArtworkDir"/>.
/// </summary>
public sealed record EditionMetadata
{
    public required string EditionId { get; init; }

    // From IGDB
    public string? IgdbName { get; init; }
    public string? IgdbSlug { get; init; }
    public string? IgdbUrl { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public int? ReleaseYear { get; init; }
    public string? CoverFile { get; init; }
    public string? KeyArtFile { get; init; }
    public string? FetchedAt { get; init; }

    // From SteamGridDB (clear logo); replaced by a refresh like IGDB's data
    public string? LogoFile { get; init; }
    public long? SteamGridDbId { get; init; }

    // From TitleDB (matched by title ID); shown first, with IGDB as the backup
    public string? TdbTitleId { get; init; }
    public string? TdbName { get; init; }
    public string? TdbSummary { get; init; }
    public string? TdbPublisher { get; init; }
    public int? TdbReleaseDate { get; init; } // yyyymmdd
    public IReadOnlyList<string> TdbGenres { get; init; } = [];
    public int? TdbPlayers { get; init; }
    public string? TdbIconFile { get; init; }
    public string? TdbBannerFile { get; init; }
    /// <summary>The eShop banner and screenshots, offered in Choose Background.</summary>
    public IReadOnlyList<string> TdbArtUrls { get; init; } = [];
    public string? TdbFetchedAt { get; init; }

    public bool HasTitleDb => TdbTitleId is not null;

    // From nx-versions: the newest known update, when it appeared, and when the game first appeared
    public long? NxLatestUpdate { get; init; }
    public int? NxUpdateDate { get; init; }   // yyyymmdd
    public int? NxFirstDate { get; init; }    // yyyymmdd; may be later than the real release

    // The user's own (never overwritten by a refresh)
    public string? UserSummary { get; init; }
    public string? UserCoverFile { get; init; }
    public string? UserKeyArtFile { get; init; }
    public string? UserLogoFile { get; init; }

    /// <summary>The description shown when the user hasn't written their own: TitleDB's, else IGDB's.</summary>
    public string? DefaultSummary => TdbSummary ?? Summary;
    public string? DisplaySummary => UserSummary ?? DefaultSummary;
    public string? DisplayCoverFile => UserCoverFile ?? TdbIconFile ?? CoverFile;
    public string? DisplayKeyArtFile => UserKeyArtFile ?? TdbBannerFile ?? KeyArtFile;
    public IReadOnlyList<string> DisplayGenres => TdbGenres.Count > 0 ? TdbGenres : Genres;
    public int? DisplayReleaseYear => TdbReleaseDate is { } d ? d / 10000 : ReleaseYear ?? NxFirstDate / 10000;
    public string? DisplayLogoFile => UserLogoFile ?? LogoFile; // IGDB has no logos: SteamGridDB or the user's

    public static EditionMetadata Empty(string editionId) => new() { EditionId = editionId };
}

public enum ArtKind
{
    Cover,
    KeyArt,
    Logo,
}
