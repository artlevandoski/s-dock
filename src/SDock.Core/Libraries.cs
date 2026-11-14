using System.Text.RegularExpressions;

namespace SDock.Core;

/// <summary>Browsing scopes. Each has its own search, filters, view and last selection.</summary>
public static class Libraries
{
    public const string Main = "main";
    public const string Arcade = "arcade";

    public static readonly IReadOnlyList<string> All = [Main, Arcade];

    public static string Label(string library) => library == Arcade ? "Arcade Archives" : "Main Library";
}

/// <summary>Arcade re-release series, recognised by title prefix.</summary>
public static partial class ArcadeSeries
{
    public const string None = "";
    public const string Aca = "aca";
    public const string Aca2 = "aca2";
    public const string AcaNeogeo = "aca_neogeo";
    public const string SegaAges = "sega_ages";

    public static string Label(string series) => series switch
    {
        Aca => "Arcade Archives",
        Aca2 => "Arcade Archives 2",
        AcaNeogeo => "ACA NEOGEO",
        SegaAges => "SEGA AGES",
        _ => "",
    };

    // "Arcade Archives 2 …" must be tested before "Arcade Archives …"; "2" must not be the start of a
    // longer number ("Arcade Archives 2020 SUPER BASEBALL" is a first-series title).
    [GeneratedRegex(@"^\s*arcade[\s_-]*archives[\s_-]*2(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Aca2Re();

    [GeneratedRegex(@"^\s*arcade[\s_-]*archives(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex AcaRe();

    [GeneratedRegex(@"^\s*aca[\s_-]*neo[\s_-]*geo(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex NeogeoRe();

    [GeneratedRegex(@"^\s*sega[\s_-]*ages(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex SegaAgesRe();

    public static string Detect(string title)
    {
        if (Aca2Re().IsMatch(title)) return Aca2;
        if (AcaRe().IsMatch(title)) return Aca;
        if (NeogeoRe().IsMatch(title)) return AcaNeogeo;
        if (SegaAgesRe().IsMatch(title)) return SegaAges;
        return None;
    }

    /// <summary>
    /// Which library a title belongs to: a manual override wins; otherwise Arcade Archives and
    /// Arcade Archives 2 go to the Arcade library, and ACA NEOGEO and SEGA AGES only when the user opts in.
    /// </summary>
    public static string LibraryFor(string title, string? manualOverride, bool includeNeogeo, bool includeSegaAges = false)
    {
        if (manualOverride is Libraries.Main or Libraries.Arcade)
            return manualOverride;
        return Detect(title) switch
        {
            Aca or Aca2 => Libraries.Arcade,
            AcaNeogeo when includeNeogeo => Libraries.Arcade,
            SegaAges when includeSegaAges => Libraries.Arcade,
            _ => Libraries.Main,
        };
    }
}
