using Avalonia.Headless.XUnit;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

/// <summary>PC versions (a game's own .exe, such as a recompilation) and the "No emulator" choice.</summary>
public sealed class ProgramGameTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Routing_runs_programs_directly_and_honours_no_emulator()
    {
        using var lib = _env.OpenLibrary();
        var profile = _env.FakeProfile();
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);
        lib.SetGenerationDefault(Generations.Switch2, profile.Id);
        var pc = lib.AddEdition("Recompiled Game", Generations.Switch2, TestEnv.FakeEmulatorExe, ContentKinds.Pc);
        var s2 = lib.AddEdition("Switch 2 Game", Generations.Switch2, _env.Game("S2.nsp"));
        var s1 = lib.AddEdition("Switch 1 Game", Generations.Switch1, _env.Game("S1.nsp"));

        Assert.Equal(new LaunchRoute(null, RouteSource.Program, Direct: true), LaunchRouter.Resolve(lib, lib.GetEdition(pc)!));
        Assert.Equal(profile.Id, LaunchRouter.Resolve(lib, lib.GetEdition(s2)!).Profile?.Id);

        lib.SetGenerationDefault(Generations.Switch2, LaunchRouter.NoEmulatorId);
        Assert.True(LaunchRouter.Resolve(lib, lib.GetEdition(s2)!) is { Direct: true, Source: RouteSource.GenerationDefault });
        Assert.Equal(profile.Id, LaunchRouter.Resolve(lib, lib.GetEdition(s1)!).Profile?.Id); // Switch 1 unaffected
        Assert.Null(LaunchRouter.DefaultFor(lib, Generations.Switch2));
        Assert.True(LaunchRouter.DefaultIsNoEmulator(lib, Generations.Switch2));

        lib.SetLaunchOverride(s1, LaunchRouter.NoEmulatorId);
        Assert.True(LaunchRouter.Resolve(lib, lib.GetEdition(s1)!) is { Direct: true, Source: RouteSource.GameChoice });
        lib.SetLaunchOverride(s1, profile.Id); // a real profile replaces "No emulator"
        Assert.Equal(profile.Id, LaunchRouter.Resolve(lib, lib.GetEdition(s1)!).Profile?.Id);
        lib.SetGenerationDefault(Generations.Switch2, profile.Id);
        Assert.False(LaunchRouter.DefaultIsNoEmulator(lib, Generations.Switch2));
    }

    [Fact]
    public void No_emulator_choices_persist()
    {
        string id;
        using (var lib = _env.OpenLibrary())
        {
            id = lib.AddEdition("Game", Generations.Switch1, _env.Game());
            lib.SetGenerationDefault(Generations.Switch2, LaunchRouter.NoEmulatorId);
            lib.SetLaunchOverride(id, LaunchRouter.NoEmulatorId);
        }
        using (var lib = _env.OpenLibrary())
        {
            Assert.Equal(LaunchRouter.NoEmulatorId, lib.GetGenerationDefault(Generations.Switch2));
            Assert.Equal(LaunchRouter.NoEmulatorId, lib.GetLaunchOverride(id));
            lib.SetLaunchOverride(id, null);
            Assert.Null(lib.GetLaunchOverride(id));
        }
    }

    [Fact]
    public void No_emulator_needs_a_program()
    {
        var direct = LaunchRouter.DirectProfile(_env.Game("Not a program.nsp"));
        Assert.Contains(LaunchArgs.Validate(direct, _env.Game("Not a program.nsp")), m => m.Contains("isn't one"));
        var exe = LaunchRouter.DirectProfile(TestEnv.FakeEmulatorExe);
        Assert.Empty(LaunchArgs.Validate(exe, TestEnv.FakeEmulatorExe)); // no {game} argument needed
    }

    [AvaloniaFact]
    public void A_pc_version_plays_with_no_emulator_set_up()
    {
        using var lib = _env.OpenLibrary(); // no emulator profiles at all
        var pc = lib.AddEdition("Recompiled Game", Generations.Switch2, TestEnv.FakeEmulatorExe, ContentKinds.Pc);
        var ui = new FakeUi();
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(pc);
        Assert.Equal("Runs on its own (PC version, no emulator)", vm.RouteText);
        Assert.Single(vm.EmulatorChoices);
        var finished = false;
        launcher.Finished += (_, code, _) => finished = code == 0;

        vm.PlayCommand.Execute(null);
        Ui.WaitUntil(() => finished, 15000);
        Assert.Empty(ui.Messages);  // no "set up an emulator" question
        Assert.Empty(ui.Pickers);   // and no emulator picker
    }

    [AvaloniaFact]
    public void Switch_2_default_can_be_no_emulator_in_emulator_settings()
    {
        using var launcher = new EmulatorLauncher();
        var vm = new EmulatorProfilesViewModel([_env.FakeProfile()], null, LaunchRouter.NoEmulatorId, launcher, new FakeUi(),
            null, _env.Root);
        Assert.Contains(vm.Switch2Choices, c => c.Id == LaunchRouter.NoEmulatorId);
        Assert.DoesNotContain(vm.Switch1Choices, c => c.Id == LaunchRouter.NoEmulatorId);
        Assert.Equal(LaunchRouter.NoEmulatorId, vm.Switch2Default?.Id);
    }

    [AvaloniaFact]
    public void Game_dropdown_offers_no_emulator_and_labels_the_default()
    {
        using var lib = _env.OpenLibrary();
        lib.SaveProfile(_env.FakeProfile());
        lib.SetGenerationDefault(Generations.Switch2, LaunchRouter.NoEmulatorId);
        var id = lib.AddEdition("Switch 2 Game", Generations.Switch2, _env.Game("S2.nsp"));
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        vm.Select(id);
        Assert.Equal("Use default (no emulator)", vm.EmulatorChoices[0].Label);
        Assert.Contains(vm.EmulatorChoices, c => c.Id == LaunchRouter.NoEmulatorId);
        Assert.Equal("Runs directly, no emulator (Switch 2 default)", vm.RouteText);
    }

    [AvaloniaFact]
    public async Task Add_game_from_an_exe()
    {
        using var lib = _env.OpenLibrary();
        var ui = new FakeUi { NextPickedFile = TestEnv.FakeEmulatorExe };
        ui.OnEditText = (_, suggested) => { Assert.False(string.IsNullOrWhiteSpace(suggested)); return "My Recomp\n"; };
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, ui);

        await vm.AddProgramCommand.ExecuteAsync(null);
        var e = Assert.Single(lib.ListEditions());
        Assert.Equal("My Recomp", e.Title);
        Assert.Equal(ContentKinds.Pc, e.ContentKind);
        Assert.Equal(Generations.Switch2, e.Generation);
        Assert.True(e.IsProgram);
        Assert.Equal("My Recomp", vm.SelectedItem?.Title);
        Assert.DoesNotContain(vm.MenuItems, m => m.Label == "Launch With…");
        vm.OpenGameMenu();
        Assert.DoesNotContain(vm.MenuItems, m => m.Label is "Launch With…" or "Play Version…" or "Add Version File…");

        ui.NextPickedFile = TestEnv.FakeEmulatorExe; // the same program again: not added twice
        await vm.AddProgramCommand.ExecuteAsync(null);
        Assert.Single(lib.ListEditions());
    }

    [Theory]
    [InlineData(@"C:\Games\Croc 2 [01006D9026E1E000]\bin\croc2.exe", "01006D9026E1E000")]
    [InlineData(@"C:\Games\recomp [01006d9026e1e000].exe", "01006D9026E1E000")]
    [InlineData(@"C:\Games\Some Game\game.exe", null)]
    public void Title_id_from_the_program_path(string path, string? expected) =>
        Assert.Equal(expected, MainViewModel.TitleIdIn(path));

    [AvaloniaFact]
    public async Task Set_title_id_checks_and_saves_it()
    {
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("My Recomp", Generations.Switch2, TestEnv.FakeEmulatorExe, ContentKinds.Pc);
        var ui = new FakeUi();
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(id);

        ui.OnEditText = (_, _) => "not an id";
        await vm.SetTitleIdCommand.ExecuteAsync(null);
        Assert.Contains(ui.Messages, m => m.Contains("isn't a title ID"));
        Assert.Null(lib.GetEdition(id)!.ConsoleTitleId);

        ui.OnEditText = (_, _) => "[01006d9026e1e000]";
        await vm.SetTitleIdCommand.ExecuteAsync(null);
        Assert.Equal("01006D9026E1E000", lib.GetEdition(id)!.ConsoleTitleId);

        ui.OnEditText = (_, _) => "";
        await vm.SetTitleIdCommand.ExecuteAsync(null);
        Assert.Null(lib.GetEdition(id)!.ConsoleTitleId);
    }
}
