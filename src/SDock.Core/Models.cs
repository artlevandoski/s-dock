namespace SDock.Core;

/// <summary>Generation values as stored in the database.</summary>
public static class Generations
{
    public const string Switch1 = "switch1";
    public const string Switch2 = "switch2";
    public const string Unclassified = "unclassified";
    public const string Both = "both"; // emulator profiles only

    /// <summary>Generations a library entry can have.</summary>
    public static readonly IReadOnlyList<string> All = [Switch1, Switch2, Unclassified];

    /// <summary>Generations an emulator profile can support.</summary>
    public static readonly IReadOnlyList<string> ProfileOptions = [Switch1, Switch2, Both];

    public static string Label(string generation) => generation switch
    {
        Switch1 => "Switch 1",
        Switch2 => "Switch 2",
        Both => "Switch 1 and Switch 2",
        _ => "Unclassified",
    };
}

public static class ContentKinds
{
    public const string Base = "base";
    public const string Update = "update";
    public const string Dlc = "dlc";
    /// <summary>A PC build or recompilation: the game is its own .exe and runs without an emulator.</summary>
    public const string Pc = "pc";

    public static string Label(string kind) => kind switch
    {
        Base => "Base game",
        Update => "Update / patch",
        Dlc => "DLC / add-on",
        Pc => "PC version (runs without an emulator)",
        _ => kind,
    };
}

public sealed record EditionFile(long Id, string EditionId, string Path, string Role = "primary")
{
    public bool Exists => File.Exists(Path);
}

/// <summary>A library entry with a stable internal ID. Local files are stored separately.</summary>
public sealed class Edition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Generation { get; init; } = Generations.Unclassified;
    public string ContentKind { get; init; } = ContentKinds.Base;
    public string? ConsoleTitleId { get; init; }
    public long? IgdbId { get; init; }

    /// <summary>The user's own sort title (e.g. "Zelda 01" or "Legend of Zelda"), or null to sort by the title.</summary>
    public string? SortTitle { get; init; }

    public string SortKey => SortTitle ?? Title;
    public List<EditionFile> Files { get; init; } = [];

    public EditionFile? PrimaryFile => Files.FirstOrDefault(f => f.Role == "primary") ?? Files.FirstOrDefault();

    /// <summary>Other files of the same game (alternate versions), launched with Play Version….</summary>
    public IEnumerable<EditionFile> Alternates => Files.Where(f => f != PrimaryFile);

    public bool HasVersions => Files.Count > 1;

    public bool IsMissing => PrimaryFile is not { Exists: true };

    /// <summary>The game's file is a program (.exe), so it runs on its own.</summary>
    public bool IsProgram => ContentKind == ContentKinds.Pc ||
                             (PrimaryFile?.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ?? false);
}

public sealed record EmulatorProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Executable { get; init; }
    public string WorkingDir { get; init; } = "";
    public string Generation { get; init; } = Generations.Switch1;
    public IReadOnlyList<string> Args { get; init; } = [];
    public string Emulator { get; init; } = "custom";

    public static EmulatorProfile New(string name, string executable, IReadOnlyList<string> args,
        string workingDir = "", string generation = Generations.Switch1, string emulator = "custom") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Executable = executable,
        Args = args,
        WorkingDir = workingDir,
        Generation = generation,
        Emulator = emulator,
    };

    public bool Supports(string generation) => Generation == Generations.Both || Generation == generation;

    public bool SameSettingsAs(EmulatorProfile other) =>
        Id == other.Id && Name == other.Name && Executable == other.Executable && WorkingDir == other.WorkingDir &&
        Generation == other.Generation && Emulator == other.Emulator && Args.SequenceEqual(other.Args);
}
