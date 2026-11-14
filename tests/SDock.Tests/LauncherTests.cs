using SDock.Core;

namespace SDock.Tests;

public sealed class LauncherTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    /// <summary>Collects launcher events (raised on the thread pool when there is no UI context).</summary>
    private sealed class Events
    {
        public readonly TaskCompletionSource<(string Tag, int Pid)> Launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<(string Tag, int Code, bool Crashed)> Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<(string Tag, string Message)> Failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Events(EmulatorLauncher l)
        {
            l.Launched += (t, p) => Launched.TrySetResult((t, p));
            l.Finished += (t, c, x) => Finished.TrySetResult((t, c, x));
            l.Failed += (t, m) => Failed.TrySetResult((t, m));
        }
    }

    [Fact]
    public void Arguments_keep_spaces_intact()
    {
        var game = _env.Game();
        Assert.Equal(["-g", game], LaunchArgs.Build(LaunchArgs.EdenPreset, game)); // one argument, not split
    }

    [Fact]
    public void Working_dir_defaults_to_exe_folder()
    {
        var p = EmulatorProfile.New("x", @"C:\Emu Dir\eden.exe", []);
        Assert.Equal(@"C:\Emu Dir", LaunchArgs.EffectiveWorkingDir(p));
    }

    [Fact]
    public void Validation_reports_missing_executable_and_game()
    {
        var p = EmulatorProfile.New("x", Path.Combine(_env.Root, "nope", "eden.exe"), LaunchArgs.EdenPreset);
        var problems = LaunchArgs.Validate(p, Path.Combine(_env.Root, "missing game.nsp"));
        Assert.Contains(problems, m => m.Contains("executable not found"));
        Assert.Contains(problems, m => m.Contains("Game file not found"));
        Assert.StartsWith("No emulator", LaunchArgs.Validate(null, "x")[0]);
    }

    [Fact]
    public void Validation_requires_game_placeholder()
    {
        var p = _env.FakeProfile() with { Args = ["--fullscreen"] };
        Assert.Contains(LaunchArgs.Validate(p, _env.Game()), m => m.Contains("{game}"));
    }

    [Fact]
    public async Task Launch_completes_with_test_process()
    {
        var game = _env.Game();
        using var launcher = new EmulatorLauncher();
        var ev = new Events(launcher);
        Assert.True(launcher.Launch(_env.FakeProfile(exitCode: 3, sleep: 0.3), game, "edition-1"));
        var launched = await ev.Launched.Task.WaitAsync(Timeout);
        Assert.Equal("edition-1", launched.Tag);
        Assert.True(launched.Pid > 0);

        Assert.Equal(("edition-1", 3, false), await ev.Finished.Task.WaitAsync(Timeout));
        Assert.False(launcher.IsRunning);
        var rec = _env.ReadRecord();
        Assert.Equal(game, rec.GetProperty("game").GetString());
        Assert.True(rec.GetProperty("game_exists").GetBoolean());
        Assert.Equal(_env.EmulatorDir, rec.GetProperty("cwd").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Launch_writes_log_file()
    {
        var log = Path.Combine(_env.Root, "last launch.log");
        using var launcher = new EmulatorLauncher(log);
        var ev = new Events(launcher);
        Assert.True(launcher.Launch(_env.FakeProfile(), _env.Game(), "e"));
        await ev.Finished.Task.WaitAsync(Timeout);
        Assert.Contains("-g", File.ReadAllText(log));
    }

    [Fact]
    public async Task Missing_executable_fails_without_starting()
    {
        using var launcher = new EmulatorLauncher();
        var ev = new Events(launcher);
        var p = EmulatorProfile.New("x", Path.Combine(_env.Root, "missing", "eden.exe"), LaunchArgs.EdenPreset);
        Assert.False(launcher.Launch(p, _env.Game(), "e1"));
        Assert.Contains("not found", (await ev.Failed.Task.WaitAsync(Timeout)).Message);
        Assert.False(launcher.IsRunning);
    }

    [Fact]
    public async Task Missing_game_fails_without_starting()
    {
        using var launcher = new EmulatorLauncher();
        var ev = new Events(launcher);
        Assert.False(launcher.Launch(_env.FakeProfile(), Path.Combine(_env.Root, "gone.nsp"), "e1"));
        Assert.Contains("Game file not found", (await ev.Failed.Task.WaitAsync(Timeout)).Message);
    }

    [Fact]
    public async Task Startup_failure_is_reported()
    {
        var bogus = Path.Combine(_env.Root, "Not Really An Emulator.exe");
        File.WriteAllText(bogus, "this is not a program");
        using var launcher = new EmulatorLauncher();
        var ev = new Events(launcher);
        Assert.False(launcher.Launch(EmulatorProfile.New("x", bogus, LaunchArgs.EdenPreset), _env.Game(), "e1"));
        var failed = await ev.Failed.Task.WaitAsync(Timeout);
        Assert.Equal("e1", failed.Tag);
        Assert.Contains("could not be started", failed.Message);
        Assert.False(launcher.IsRunning);
    }

    [Fact]
    public async Task Second_launch_rejected_while_running()
    {
        var game = _env.Game();
        using var launcher = new EmulatorLauncher();
        var ev = new Events(launcher);
        Assert.True(launcher.Launch(_env.FakeProfile(sleep: 1.0), game, "a"));
        await ev.Launched.Task.WaitAsync(Timeout);
        Assert.False(launcher.Launch(_env.FakeProfile(), game, "b"));
        Assert.Equal("b", (await ev.Failed.Task.WaitAsync(Timeout)).Tag);
        Assert.Equal("a", (await ev.Finished.Task.WaitAsync(Timeout)).Tag);
    }
}
