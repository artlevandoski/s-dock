using Velopack;
using Velopack.Sources;

namespace SDock.App.Updates;

/// <summary>Finding, downloading and applying a newer S-Dock (faked in tests).</summary>
public interface IAppUpdater
{
    /// <summary>False when running from source or a plain build: updates only work in the released S-Dock folder.</summary>
    bool CanUpdate { get; }
    string? CurrentVersion { get; }
    /// <summary>The newer version's number, or null when this is the newest.</summary>
    Task<string?> CheckAsync(CancellationToken ct = default);
    /// <summary>Downloads the version <see cref="CheckAsync"/> found (into the S-Dock folder's packages folder).</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken ct = default);
    /// <summary>Closes S-Dock, swaps in the new version and starts it again.</summary>
    void ApplyAndRestart();
    /// <summary>Closes S-Dock and swaps in the new version without starting it (for --update-now).</summary>
    void ApplyAndExit();
}

/// <summary>
/// Updates through Velopack from the GitHub releases (pre-releases included while S-Dock is in beta). The release
/// zip is Velopack's portable layout, so an update replaces only the "current" folder: data\ and emulators\
/// beside it are never touched, and nothing is written outside the S-Dock folder.
/// SDOCK_UPDATE_SOURCE can point at a folder or URL with releases.win.json instead (for testing an update).
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    public const string RepoUrl = "https://github.com/artlevandoski/s-dock";
    public const string SourceEnv = "SDOCK_UPDATE_SOURCE";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    public VelopackUpdater()
    {
        try
        {
            var source = Environment.GetEnvironmentVariable(SourceEnv);
            _manager = string.IsNullOrWhiteSpace(source)
                ? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: true))
                : new UpdateManager(source);
        }
        catch (Exception)
        {
            _manager = null; // not a Velopack install, or Velopack unavailable: no updates
        }
    }

    public bool CanUpdate => _manager is { IsInstalled: true };
    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    public async Task<string?> CheckAsync(CancellationToken ct = default)
    {
        if (!CanUpdate) return null;
        _found = await _manager!.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken ct = default) =>
        _found is null || _manager is null ? Task.CompletedTask : _manager.DownloadUpdatesAsync(_found, progress, ct);

    public void ApplyAndRestart()
    {
        if (_found is not null) _manager?.ApplyUpdatesAndRestart(_found.TargetFullRelease, []);
    }

    public void ApplyAndExit()
    {
        if (_found is not null) _manager?.ApplyUpdatesAndExit(_found.TargetFullRelease);
    }
}
