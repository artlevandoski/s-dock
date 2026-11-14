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

    // The user's own (never overwritten by a refresh)
    public string? UserSummary { get; init; }
    public string? UserCoverFile { get; init; }
    public string? UserKeyArtFile { get; init; }
    public string? UserLogoFile { get; init; }

    public string? DisplaySummary => UserSummary ?? Summary;
    public string? DisplayCoverFile => UserCoverFile ?? CoverFile;
    public string? DisplayKeyArtFile => UserKeyArtFile ?? KeyArtFile;
    public string? DisplayLogoFile => UserLogoFile; // IGDB has no game logos; logos come from the user

    public static EditionMetadata Empty(string editionId) => new() { EditionId = editionId };
}

public enum ArtKind
{
    Cover,
    KeyArt,
    Logo,
}
