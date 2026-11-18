using System.Runtime.CompilerServices;
using SDock.Core;

namespace SDock.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Runs before any test: points S-Dock's data folder (database, logs, artwork) at a throwaway
    /// temp folder, so tests never write into the repository's real data\ folder.
    /// </summary>
    [ModuleInitializer]
    internal static void IsolateDataFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "S-Dock Tests", "app-data-" + Environment.ProcessId);
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, dir);
        // EmuReady is on by default: tests must never reach the real site.
        SDock.App.ViewModels.MainViewModel.EmuReadyFactory = () => new SDock.Core.Metadata.EmuReadyClient(new FakeEmuReady { Offline = true });
    }
}
