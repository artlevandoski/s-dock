namespace SDock.Core;

public enum RouteSource
{
    /// <summary>The game has its own emulator choice.</summary>
    GameChoice,
    /// <summary>The default profile for the game's generation.</summary>
    GenerationDefault,
    /// <summary>Nothing set: ask the user with the profile picker.</summary>
    AskUser,
}

public sealed record LaunchRoute(EmulatorProfile? Profile, RouteSource Source);

/// <summary>Decides which emulator profile Play uses: the game's choice, then the generation default, then ask.</summary>
public static class LaunchRouter
{
    public static LaunchRoute Resolve(LibraryStore library, Edition edition)
    {
        var profiles = library.ListProfiles().ToDictionary(p => p.Id);

        if (library.GetLaunchOverride(edition.Id) is { } choiceId && profiles.TryGetValue(choiceId, out var chosen))
            return new LaunchRoute(chosen, RouteSource.GameChoice);

        if (edition.Generation is Generations.Switch1 or Generations.Switch2 &&
            library.GetGenerationDefault(edition.Generation) is { } defaultId &&
            profiles.TryGetValue(defaultId, out var fallback))
            return new LaunchRoute(fallback, RouteSource.GenerationDefault);

        return new LaunchRoute(null, RouteSource.AskUser);
    }

    /// <summary>The profile the game would use if it had no choice of its own (for "Use default (…)" labels).</summary>
    public static EmulatorProfile? DefaultFor(LibraryStore library, string generation) =>
        generation is Generations.Switch1 or Generations.Switch2 && library.GetGenerationDefault(generation) is { } id
            ? library.GetProfile(id)
            : null;
}
