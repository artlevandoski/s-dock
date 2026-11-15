using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Milestone 2: named profiles, generation defaults, per-game choice, picker and Launch With….</summary>
public sealed class RoutingTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (LibraryStore Lib, Edition Game, EmulatorProfile Stable, EmulatorProfile Nightly) Setup(string generation = Generations.Switch1)
    {
        var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Super Placeholder Quest", generation, _env.Game());
        // Two versions of the same emulator side by side.
        var stable = EmulatorProfile.New("Eden 0.2.1", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        var nightly = EmulatorProfile.New("Eden nightly", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        lib.SaveProfile(stable);
        lib.SaveProfile(nightly);
        return (lib, lib.GetEdition(id)!, stable, nightly);
    }

    [Fact]
    public void Play_order_is_game_choice_then_generation_default_then_ask()
    {
        var (lib, game, stable, nightly) = Setup();
        using var _ = lib;

        Assert.Equal(new LaunchRoute(null, RouteSource.AskUser), LaunchRouter.Resolve(lib, game));

        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        var byDefault = LaunchRouter.Resolve(lib, game);
        Assert.Equal(RouteSource.GenerationDefault, byDefault.Source);
        Assert.Equal(stable.Id, byDefault.Profile!.Id);

        lib.SetLaunchOverride(game.Id, nightly.Id);
        var byChoice = LaunchRouter.Resolve(lib, game);
        Assert.Equal(RouteSource.GameChoice, byChoice.Source);
        Assert.Equal(nightly.Id, byChoice.Profile!.Id);

        lib.SetLaunchOverride(game.Id, null);
        Assert.Equal(RouteSource.GenerationDefault, LaunchRouter.Resolve(lib, game).Source);
    }

    [Fact]
    public void Unclassified_games_have_no_generation_default()
    {
        var (lib, game, stable, _) = Setup(Generations.Unclassified);
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        Assert.Equal(RouteSource.AskUser, LaunchRouter.Resolve(lib, game).Source);
        Assert.Throws<ArgumentException>(() => lib.SetGenerationDefault(Generations.Unclassified, stable.Id));
    }

    [Fact]
    public void Removing_a_profile_clears_games_and_defaults_that_used_it()
    {
        var (lib, game, stable, nightly) = Setup();
        using var _ = lib;
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        lib.SetLaunchOverride(game.Id, nightly.Id);

        lib.DeleteProfile(nightly.Id);
        Assert.Null(lib.GetLaunchOverride(game.Id));
        Assert.Equal(stable.Id, LaunchRouter.Resolve(lib, game).Profile!.Id); // falls back to the default

        lib.DeleteProfile(stable.Id);
        Assert.Null(lib.GetGenerationDefault(Generations.Switch1));
        Assert.Equal(RouteSource.AskUser, LaunchRouter.Resolve(lib, game).Source);
    }

    [Fact]
    public void Removing_a_game_clears_its_emulator_choice()
    {
        var (lib, game, stable, _) = Setup();
        using var __ = lib;
        lib.SetLaunchOverride(game.Id, stable.Id);
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON; DELETE FROM editions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", game.Id);
            cmd.ExecuteNonQuery();
        }
        Assert.Null(lib.GetLaunchOverride(game.Id));
    }

    [Fact]
    public void Failed_settings_save_changes_nothing()
    {
        var (lib, _, stable, nightly) = Setup();
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        var broken = EmulatorProfile.New("Broken", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset) with { Generation = "bogus" };

        // Remove "stable" and add a profile that can't be saved: the removal must be rolled back.
        Assert.Throws<ArgumentException>(() => lib.SaveEmulatorSettings([stable.Id], [nightly, broken], nightly.Id, null));
        Assert.NotNull(lib.GetProfile(stable.Id));
        Assert.Equal(stable.Id, lib.GetGenerationDefault(Generations.Switch1));
        Assert.Equal(2, lib.ListProfiles().Count);

        // The store is still usable afterwards.
        lib.SaveEmulatorSettings([stable.Id], [nightly], nightly.Id, null);
        Assert.Equal(nightly.Id, Assert.Single(lib.ListProfiles()).Id);
        Assert.Equal(nightly.Id, lib.GetGenerationDefault(Generations.Switch1));
    }

    // ------------------------------------------------------------- upgrade
    private void DowngradeToV1()
    {
        using var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DROP TABLE generation_defaults; DROP TABLE edition_launch_overrides; " +
                          "UPDATE meta SET value = '1' WHERE key = 'schema_version';";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Upgrade_makes_a_lone_profile_the_switch1_default()
    {
        var eden = EmulatorProfile.New("Eden", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        using (var lib = _env.OpenLibrary())
            lib.SaveProfile(eden);
        DowngradeToV1();

        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Equal(eden.Id, upgraded.GetGenerationDefault(Generations.Switch1));
        Assert.Null(upgraded.GetGenerationDefault(Generations.Switch2)); // Eden profile is Switch 1 only
    }

    [Fact]
    public void Upgrade_with_several_profiles_sets_no_default()
    {
        using (var lib = _env.OpenLibrary())
        {
            lib.SaveProfile(EmulatorProfile.New("A", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset));
            lib.SaveProfile(EmulatorProfile.New("B", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset));
        }
        DowngradeToV1();
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        Assert.Null(upgraded.GetGenerationDefault(Generations.Switch1));
    }

    [Fact]
    public void New_database_starts_at_current_version()
    {
        using var lib = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, lib.StoredSchemaVersion);
    }

    // ----------------------------------------------------------- discovery
    [Fact]
    public void Discovery_finds_builds_and_skips_helpers()
    {
        var dir = _env.CreateEmulatorsFolder();
        var found = EmulatorDiscovery.Find(dir);
        Assert.Equal(["Eden 0.2.1", "Eden nightly", "Other Build"], found.Select(f => f.SuggestedName));
        Assert.True(found[0].LooksLikeEden);
        Assert.False(found[2].LooksLikeEden);
        Assert.DoesNotContain(found, f => f.Path.Contains("unins000") || f.Path.Contains("crashpad") || f.Path.Contains("eden-cli"));
        Assert.Empty(EmulatorDiscovery.Find(Path.Combine(_env.Root, "does not exist")));

        // Two GUI programs in one folder get distinct names.
        var extra = Path.Combine(dir, "Other Build", "othernx-qt.exe");
        File.Copy(TestEnv.FakeEmulatorExe, extra);
        Assert.Equal(["Eden 0.2.1", "Eden nightly", "Other Build (othernx-qt.exe)", "Other Build (othernx.exe)"],
            EmulatorDiscovery.Find(dir).Select(f => f.SuggestedName));
    }

    // -------------------------------------------------- view-model behavior
    private static (MainViewModel Vm, FakeUi Ui, EmulatorLauncher Launcher) Window(LibraryStore lib, string selectId)
    {
        var ui = new FakeUi();
        var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(selectId);
        return (vm, ui, launcher);
    }

    [AvaloniaFact]
    public async Task Picker_can_remember_choice_for_game_and_as_default()
    {
        var (lib, game, _, nightly) = Setup();
        using var __ = lib;
        var (vm, ui, launcher) = Window(lib, game.Id);
        using var ___ = launcher;
        Assert.Equal("Play will ask which emulator to use", vm.RouteText);

        ui.OnPick = p =>
        {
            Assert.False(p.OneTime);
            Assert.True(p.ShowRemember && p.ShowMakeDefault);
            p.Selected = p.Items.Single(i => i.Profile.Id == nightly.Id);
            p.RememberForGame = true;
            p.MakeDefault = true;
            return true;
        };
        Assert.True(await vm.PlaySelectedAsync());
        Assert.Equal(nightly.Id, lib.GetLaunchOverride(game.Id));
        Assert.Equal(nightly.Id, lib.GetGenerationDefault(Generations.Switch1));
        Assert.Equal("Plays with Eden nightly (this game's choice)", vm.RouteText);
        await WaitForExit(vm);
    }

    [AvaloniaFact]
    public async Task Launch_with_is_one_time_and_saves_nothing()
    {
        var (lib, game, stable, nightly) = Setup();
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        var (vm, ui, launcher) = Window(lib, game.Id);
        using var ___ = launcher;

        ui.OnPick = p =>
        {
            Assert.True(p.OneTime);
            Assert.False(p.ShowRemember || p.ShowMakeDefault);
            p.Selected = p.Items.Single(i => i.Profile.Id == nightly.Id);
            return true;
        };
        Assert.True(await vm.LaunchSelectedWithAsync());
        Assert.Null(lib.GetLaunchOverride(game.Id));
        Assert.Equal(stable.Id, lib.GetGenerationDefault(Generations.Switch1));
        await WaitForExit(vm);

        // Next plain Play goes back to the default without asking.
        ui.Pickers.Clear();
        Assert.True(await vm.PlaySelectedAsync());
        Assert.Empty(ui.Pickers);
        await WaitForExit(vm);
    }

    [AvaloniaFact]
    public void Details_dropdown_sets_and_clears_the_game_choice()
    {
        var (lib, game, stable, nightly) = Setup();
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);
        var (vm, _, launcher) = Window(lib, game.Id);
        using var ___ = launcher;

        Assert.Equal(["Use default (Eden 0.2.1)", "Eden 0.2.1", "Eden nightly", "No emulator (run the game's .exe)"], vm.EmulatorChoices.Select(c => c.Label));
        vm.SelectedEmulatorChoice = vm.EmulatorChoices.Single(c => c.Id == nightly.Id);
        Assert.Equal(nightly.Id, lib.GetLaunchOverride(game.Id));
        vm.SelectedEmulatorChoice = vm.EmulatorChoices[0];
        Assert.Null(lib.GetLaunchOverride(game.Id));
    }

    [AvaloniaFact]
    public void Cancelling_the_picker_launches_nothing()
    {
        var (lib, game, _, _) = Setup();
        using var __ = lib;
        var (vm, ui, launcher) = Window(lib, game.Id);
        using var ___ = launcher;
        Assert.False(vm.PlaySelectedAsync().GetAwaiter().GetResult());
        Assert.Single(ui.Pickers);
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public void Settings_add_remove_and_defaults_apply_to_library()
    {
        var (lib, _, stable, nightly) = Setup();
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, nightly.Id);
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new EmulatorProfilesViewModel(lib.ListProfiles(), nightly.Id, null, launcher, ui, null, _env.CreateEmulatorsFolder());

        Assert.Equal(3, vm.Discovered.Count);
        Assert.Equal(["Ask each time", "Eden 0.2.1", "Eden nightly"], vm.Switch1Choices.Select(c => c.Label));
        Assert.Equal(nightly.Id, vm.Switch1Default!.Id);

        // Remove nightly (confirm), add a build from the Emulators folder.
        vm.Selected = vm.Profiles.Single(p => p.Id == nightly.Id);
        ui.ConfirmResult = true;
        vm.RemoveProfileCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Null(vm.Switch1Default!.Id); // default went with it
        vm.SelectedDiscovered = vm.Discovered.Single(d => d.SuggestedName == "Other Build");
        vm.AddDiscoveredCommand.Execute(null);
        var added = vm.Selected!;
        Assert.Equal("Other Build", added.DisplayName);
        Assert.Equal([LaunchArgs.GamePlaceholder], added.Args); // unknown emulator: no unverified preset
        Assert.Equal(added.Id, vm.Switch1Default!.Id);          // adopted as the empty Switch 1 default

        vm.ApplyTo(lib);
        Assert.Equal(["Eden 0.2.1", "Other Build"], lib.ListProfiles().Select(p => p.Name).Order());
        Assert.Null(lib.GetProfile(nightly.Id));
        Assert.Equal(added.Id, lib.GetGenerationDefault(Generations.Switch1));
        vm.Detach();
    }

    /// <summary>Regression: closing the real window used to blank the dropdowns before ApplyTo ran (crash on Save).</summary>
    [AvaloniaFact]
    public void Save_through_the_real_window_keeps_every_setting()
    {
        var (lib, _, stable, nightly) = Setup();
        using var __ = lib;
        lib.SetGenerationDefault(Generations.Switch1, nightly.Id);
        using var launcher = new EmulatorLauncher();
        var vm = new EmulatorProfilesViewModel(lib.ListProfiles(), nightly.Id, null, launcher, new FakeUi(), null, _env.CreateEmulatorsFolder());
        var window = new SDock.App.Views.EmulatorSettingsWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Edit a profile, switch selection (rebinds the editor), then Save, which closes the window.
        vm.Selected = vm.Profiles.Single(p => p.Id == stable.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.Selected!.Name = "Eden 0.2.1 (stable)";
        vm.Selected.Generation = GenerationOption.ForProfile(Generations.Both);
        vm.Selected = vm.Profiles.Single(p => p.Id == nightly.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);

        vm.ApplyTo(lib);
        vm.Detach();
        var saved = lib.GetProfile(stable.Id)!;
        Assert.Equal("Eden 0.2.1 (stable)", saved.Name);
        Assert.Equal(Generations.Both, saved.Generation);
        Assert.Equal(Generations.Switch1, lib.GetProfile(nightly.Id)!.Generation);
        Assert.Equal(nightly.Id, lib.GetGenerationDefault(Generations.Switch1)); // not reset to "Ask each time"
    }

    private static async Task WaitForExit(MainViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (vm.IsRunning)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }
}
