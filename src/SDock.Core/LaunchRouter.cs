namespace SDock.Core;

public enum RouteSource
{
    /// <summary>The game has its own emulator choice.</summary>
    GameChoice,
    /// <summary>The default profile for the game's generation.</summary>
    GenerationDefault,
    /// <summary>Nothing set: ask the user with the profile picker.</summary>
    AskUser,
    /// <summary>The game is a program (.exe): it always runs on its own.</summary>
    Program,
}

/// <summary>How Play starts a game: with an emulator profile, or directly (no emulator) when <see cref="Direct"/>.</summary>
public sealed record LaunchRoute(EmulatorProfile? Profile, RouteSource Source, bool Direct = false);

/// <summary>Decides which emulator profile Play uses: the game's choice, then the generation default, then ask.</summary>
public static class LaunchRouter
{
    /// <summary>The "No emulator" choice, in the emulator dropdowns and wherever a profile ID is stored.</summary>
    public const string NoEmulatorId = "none";

    public static LaunchRoute Resolve(LibraryStore library, Edition edition)
    {
        if (edition.IsProgram)
            return new LaunchRoute(null, RouteSource.Program, Direct: true);

        var choice = library.GetLaunchOverride(edition.Id);
        if (choice == NoEmulatorId)
            return new LaunchRoute(null, RouteSource.GameChoice, Direct: true);
        var profiles = library.ListProfiles().ToDictionary(p => p.Id);
        if (choice is not null && profiles.TryGetValue(choice, out var chosen))
            return new LaunchRoute(chosen, RouteSource.GameChoice);

        if (edition.Generation is Generations.Switch1 or Generations.Switch2)
        {
            var defaultId = library.GetGenerationDefault(edition.Generation);
            if (defaultId == NoEmulatorId)
                return new LaunchRoute(null, RouteSource.GenerationDefault, Direct: true);
            if (defaultId is not null && profiles.TryGetValue(defaultId, out var fallback))
                return new LaunchRoute(fallback, RouteSource.GenerationDefault);
        }

        return new LaunchRoute(null, RouteSource.AskUser);
    }

    /// <summary>The profile the game would use if it had no choice of its own (for "Use default (…)" labels).</summary>
    public static EmulatorProfile? DefaultFor(LibraryStore library, string generation) =>
        generation is Generations.Switch1 or Generations.Switch2 && library.GetGenerationDefault(generation) is { } id &&
        id != NoEmulatorId
            ? library.GetProfile(id)
            : null;

    /// <summary>True when the generation's default is "No emulator".</summary>
    public static bool DefaultIsNoEmulator(LibraryStore library, string generation) =>
        generation is Generations.Switch1 or Generations.Switch2 && library.GetGenerationDefault(generation) == NoEmulatorId;

    /// <summary>
    /// A stand-in profile that runs the game's program itself (from its own folder, with no arguments), for the
    /// launcher. <see cref="LaunchArgs.Validate"/> knows it needs no {game} argument.
    /// </summary>
    public static EmulatorProfile DirectProfile(string program) =>
        EmulatorProfile.New("No emulator", program, [], Path.GetDirectoryName(program) ?? "", Generations.Both, emulator: NoEmulatorId);
}
