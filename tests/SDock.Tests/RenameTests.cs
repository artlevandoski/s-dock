using SDock.Core;

namespace SDock.Tests;

/// <summary>The app was called Switch Yard before S-Dock; the old names keep working.</summary>
public sealed class RenameTests
{
    [Fact]
    public void Old_switch_yard_environment_names_still_work()
    {
        const string old = "SWITCHYARD_EMULATORS_DIR";
        var before = (Environment.GetEnvironmentVariable(AppPaths.EmulatorsDirEnv), Environment.GetEnvironmentVariable(old));
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.EmulatorsDirEnv, null);
            Environment.SetEnvironmentVariable(old, "C:/Old Name Emulators");
            Assert.Equal("C:/Old Name Emulators", AppPaths.Env(AppPaths.EmulatorsDirEnv));
            Environment.SetEnvironmentVariable(AppPaths.EmulatorsDirEnv, "C:/New Name Emulators");
            Assert.Equal("C:/New Name Emulators", AppPaths.Env(AppPaths.EmulatorsDirEnv)); // the new name wins
            Assert.EndsWith("SwitchYard", AppPaths.OldUserProfileDir);
            Assert.EndsWith("S-Dock", AppPaths.UserProfileDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.EmulatorsDirEnv, before.Item1);
            Environment.SetEnvironmentVariable(old, before.Item2);
        }
    }

    [Fact]
    public void Source_folder_is_found_by_either_solution_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "S-Dock Tests", "sln-" + Guid.NewGuid().ToString("N")[..8]);
        var deep = Path.Combine(root, "src", "bin");
        Directory.CreateDirectory(deep);
        try
        {
            File.WriteAllText(Path.Combine(root, "SwitchYard.sln"), "");
            Assert.Equal(root, AppPaths.FindSourceRoot(deep));
            File.Delete(Path.Combine(root, "SwitchYard.sln"));
            File.WriteAllText(Path.Combine(root, "SDock.sln"), "");
            Assert.Equal(root, AppPaths.FindSourceRoot(deep));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
