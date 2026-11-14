using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using SwitchYard.App.Input;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

namespace SwitchYard.Tests;

/// <summary>Milestone 3: libraries, arcade series, search/filters, versions, carousel and controller input.</summary>
public sealed class LibrariesTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    // ----------------------------------------------------------- classification
    [Theory]
    [InlineData("Arcade Archives DONKEY KONG", ArcadeSeries.Aca)]
    [InlineData("ARCADE ARCHIVES Mario Bros.", ArcadeSeries.Aca)]
    [InlineData("Arcade Archives 2020 SUPER BASEBALL", ArcadeSeries.Aca)] // a number, not series 2
    [InlineData("Arcade Archives 2 RIDGE RACER", ArcadeSeries.Aca2)]
    [InlineData("Arcade_Archives_2 Something", ArcadeSeries.Aca2)]
    [InlineData("ACA NEOGEO METAL SLUG", ArcadeSeries.AcaNeogeo)]
    [InlineData("ACA NEO GEO SAMURAI SHODOWN", ArcadeSeries.AcaNeogeo)]
    [InlineData("Arcade Archivesque Adventure", ArcadeSeries.None)]
    [InlineData("Kirby's Return to Dream Land Deluxe", ArcadeSeries.None)]
    [InlineData("The Arcade Archives Story", ArcadeSeries.None)] // prefix only
    public void Arcade_series_by_title_prefix(string title, string expected) =>
        Assert.Equal(expected, ArcadeSeries.Detect(title));

    [Fact]
    public void Library_placement_with_overrides_and_neogeo_option()
    {
        Assert.Equal(Libraries.Arcade, ArcadeSeries.LibraryFor("Arcade Archives 2 RIDGE RACER", null, false));
        Assert.Equal(Libraries.Main, ArcadeSeries.LibraryFor("ACA NEOGEO METAL SLUG", null, includeNeogeo: false));
        Assert.Equal(Libraries.Arcade, ArcadeSeries.LibraryFor("ACA NEOGEO METAL SLUG", null, includeNeogeo: true));
        Assert.Equal(Libraries.Main, ArcadeSeries.LibraryFor("Arcade Archives DONKEY KONG", Libraries.Main, false));
        Assert.Equal(Libraries.Arcade, ArcadeSeries.LibraryFor("Some Game", Libraries.Arcade, false));
    }

    [Fact]
    public void Library_overrides_and_neogeo_setting_persist()
    {
        string id;
        using (var lib = _env.OpenLibrary())
        {
            id = lib.AddEdition("Arcade Archives DONKEY KONG", Generations.Switch1, _env.Game());
            lib.SetLibraryOverride(id, Libraries.Main);
            lib.IncludeAcaNeogeo = true;
        }
        using (var lib = _env.OpenLibrary())
        {
            Assert.Equal(Libraries.Main, lib.GetLibraryOverride(id));
            Assert.True(lib.IncludeAcaNeogeo);
            Assert.Equal(Libraries.Main, lib.LibraryOf(lib.GetEdition(id)!));
            lib.SetLibraryOverride(id, null);
            Assert.Equal(Libraries.Arcade, lib.LibraryOf(lib.GetEdition(id)!));
            Assert.Throws<ArgumentException>(() => lib.SetLibraryOverride(id, "elsewhere"));
        }
    }

    [Fact]
    public void Upgrade_from_v2_adds_library_table()
    {
        using (var lib = _env.OpenLibrary()) { }
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE edition_library_overrides; UPDATE meta SET value = '2' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(3, upgraded.StoredSchemaVersion);
        Assert.Empty(upgraded.LibraryOverrides());
    }

    // ---------------------------------------------------------------- versions
    [Fact]
    public void Versions_add_remove_and_choose_main_file()
    {
        using var lib = _env.OpenLibrary();
        var v1 = _env.Game("Game [v0].nsp");
        var v2 = _env.Game("Game [v65536].nsp");
        var id = lib.AddEdition("Game", Generations.Switch1, v1);
        var fileId = lib.AddFile(id, v2);

        var e = lib.GetEdition(id)!;
        Assert.True(e.HasVersions);
        Assert.Equal(v1, e.PrimaryFile!.Path);
        Assert.Equal(v2, Assert.Single(e.Alternates).Path);
        Assert.Equal(id, lib.FindEditionByPath(v2)); // repeat scans recognise version files too

        Assert.Throws<InvalidOperationException>(() => lib.AddFile(id, v2));
        lib.SetPrimaryFile(fileId);
        Assert.Equal(v2, lib.GetEdition(id)!.PrimaryFile!.Path);

        lib.RemoveFile(fileId); // removing the main file promotes the other
        e = lib.GetEdition(id)!;
        Assert.Equal(v1, Assert.Single(e.Files).Path);
        Assert.Equal("primary", e.Files[0].Role);
        Assert.Throws<InvalidOperationException>(() => lib.RemoveFile(e.Files[0].Id));
        Assert.True(File.Exists(v2)); // files on disk are never touched
    }

    [Fact]
    public void Switch2_editions_stay_separate_from_switch1_titles()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Puzzle Thing", Generations.Switch1, _env.Game("Puzzle Thing.nsp"));
        var folder = Path.Combine(_env.Root, "S2 folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Puzzle Thing.nsp"), "x");
        File.WriteAllText(Path.Combine(folder, "Puzzle Thing (Switch 2 Edition).nsp"), "x");

        var asSwitch2 = LibraryScanner.ScanFolder(folder, Generations.Switch2, lib);
        Assert.All(asSwitch2.Entries, e => Assert.DoesNotContain(ScanFlags.Duplicate, e.Flags));

        var asSwitch1 = LibraryScanner.ScanFolder(folder, Generations.Switch1, lib);
        var dup = asSwitch1.Entries.Single(e => e.FileName == "Puzzle Thing.nsp");
        Assert.Contains(ScanFlags.Duplicate, dup.Flags);
        Assert.True(dup.CanAddAsVersion);
        Assert.Equal("Puzzle Thing", dup.DuplicateOfTitle);
    }

    [Fact]
    public void Import_can_attach_duplicates_as_versions()
    {
        using var lib = _env.OpenLibrary();
        var existingId = lib.AddEdition("Super Placeholder Quest", Generations.Unclassified, _env.Game("Super Placeholder Quest.nsp"));
        var folder = _env.CreateSampleFolder();

        var scan = LibraryScanner.ScanFolder(folder, Generations.Unclassified, lib);
        var vsLibrary = scan.Entries.Single(e => e.FileName == "Super Placeholder Quest [0100AAAA00000000][v0].nsp");
        Assert.Equal(existingId, vsLibrary.DuplicateOfEditionId);
        var inScan = scan.Entries.Single(e => e.Flags.Contains(ScanFlags.Duplicate) && e.Title == "Another Test Game");
        Assert.NotNull(inScan.DuplicateOfPath);
        vsLibrary.AddAsVersion = true;
        inScan.AddAsVersion = true;

        var summary = LibraryScanner.ImportEntries(lib, folder, Generations.Unclassified, scan.Entries);
        Assert.Equal(2, summary.AddedVersions);
        Assert.Equal(2, lib.GetEdition(existingId)!.Files.Count);
        var another = lib.ListEditions().Single(e => e.Title == "Another Test Game");
        Assert.Equal(2, another.Files.Count);

        // A repeat scan recognises the version files as already in the library.
        var again = LibraryScanner.ScanFolder(folder, Generations.Unclassified, lib);
        Assert.True(again.Entries.Single(e => e.Path == vsLibrary.Path).InLibrary);
    }

    // ------------------------------------------------------- libraries in the UI
    private (LibraryStore Lib, MainViewModel Vm, FakeUi Ui, EmulatorLauncher Launcher) Window()
    {
        var lib = _env.OpenLibrary();
        var ui = new FakeUi();
        var launcher = new EmulatorLauncher();
        return (lib, new MainViewModel(lib, launcher, ui), ui, launcher);
    }

    private static void Seed(LibraryStore lib, TestEnv env)
    {
        lib.AddEdition("Super Placeholder Quest", Generations.Switch1, env.Game("Super Placeholder Quest.nsp"));
        lib.AddEdition("Another Test Game", Generations.Switch2, env.Game("Another Test Game.nsp"));
        lib.AddEdition("Arcade Archives DONKEY KONG", Generations.Switch1, env.Game("Arcade Archives DONKEY KONG.nsp"));
        lib.AddEdition("Arcade Archives 2 RIDGE RACER", Generations.Switch2, env.Game("Arcade Archives 2 RIDGE RACER.nsp"));
        lib.AddEdition("ACA NEOGEO METAL SLUG", Generations.Switch1, env.Game("ACA NEOGEO METAL SLUG.nsp"));
    }

    [AvaloniaFact]
    public void Libraries_are_separate_scopes_with_their_own_state()
    {
        using (var seed = _env.OpenLibrary()) Seed(seed, _env);
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;

        Assert.Equal(Libraries.Main, vm.CurrentLibrary);
        Assert.Equal([3, 2], vm.LibraryTabs.Select(t => t.Count)); // NEOGEO stays in Main by default
        Assert.Equal(["ACA NEOGEO METAL SLUG", "Another Test Game", "Super Placeholder Quest"], vm.Items.Select(i => i.Title));

        // Main: carousel view, Switch 1 filter, a selection.
        vm.IsCarousel = true;
        vm.GenerationFilter = FilterOption.Generation.Single(o => o.Value == Generations.Switch1);
        vm.Select(vm.Items.Single(i => i.Title == "Super Placeholder Quest").Id);
        vm.SearchText = "quest";
        Assert.Equal(["Super Placeholder Quest"], vm.Items.Select(i => i.Title));

        // Arcade: grid, its own selection, series filter.
        vm.SelectedLibraryTab = vm.LibraryTabs[1];
        Assert.True(vm.IsGrid);
        Assert.Equal("", vm.SearchText);
        Assert.Equal(FilterOption.AllValue, vm.GenerationFilter.Value);
        Assert.Equal(2, vm.Items.Count);
        vm.SeriesFilter = vm.SeriesFilterOptions.Single(o => o.Value == ArcadeSeries.Aca2);
        Assert.Equal("Arcade Archives 2 RIDGE RACER", Assert.Single(vm.Items).Title);

        // Back to Main: its view, filter, search and selection come back.
        vm.SelectedLibraryTab = vm.LibraryTabs[0];
        Assert.True(vm.IsCarousel);
        Assert.Equal(Generations.Switch1, vm.GenerationFilter.Value);
        Assert.Equal("quest", vm.SearchText);
        Assert.Equal("Super Placeholder Quest", vm.SelectedTitle);

        // Restart: view, filters and current library are remembered (search is per session).
        vm.SelectedLibraryTab = vm.LibraryTabs[1];
        var again = new MainViewModel(lib, launcher, new FakeUi());
        Assert.Equal(Libraries.Arcade, again.CurrentLibrary);
        Assert.Equal(ArcadeSeries.Aca2, again.SeriesFilter.Value);
        again.SelectedLibraryTab = again.LibraryTabs[0];
        Assert.True(again.IsCarousel);
        Assert.Equal("Super Placeholder Quest", again.SelectedTitle);
    }

    [AvaloniaFact]
    public void Switching_views_keeps_the_selection()
    {
        using (var seed = _env.OpenLibrary()) Seed(seed, _env);
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.Select(vm.Items.Single(i => i.Title == "Another Test Game").Id);
        Dispatcher.UIThread.RunJobs();
        vm.ToggleViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsCarousel);
        var carousel = window.FindControl<ListBox>("CarouselList")!;
        Assert.Equal("Another Test Game", ((EditionItem)carousel.SelectedItem!).Title);
        window.Close();
    }

    [AvaloniaFact]
    public void Neogeo_option_and_manual_override_move_games()
    {
        using (var seed = _env.OpenLibrary()) Seed(seed, _env);
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;

        vm.SelectedLibraryTab = vm.LibraryTabs[1];
        vm.IncludeAcaNeogeo = true;
        Assert.Contains(vm.Items, i => i.Title == "ACA NEOGEO METAL SLUG");
        Assert.Equal([2, 3], vm.LibraryTabs.Select(t => t.Count));

        // Pin an arcade title to Main Library: the view follows it there.
        vm.Select(vm.Items.Single(i => i.Title == "Arcade Archives DONKEY KONG").Id);
        Assert.Equal("Automatic (Arcade Archives)", vm.LibraryChoices[0].Label);
        vm.SelectedLibraryChoice = vm.LibraryChoices.Single(c => c.Id == Libraries.Main);
        Assert.Equal(Libraries.Main, vm.CurrentLibrary);
        Assert.Equal("Arcade Archives DONKEY KONG", vm.SelectedTitle);
        Assert.Equal(Libraries.Main, lib.GetLibraryOverride(vm.SelectedId!));
    }

    [AvaloniaFact]
    public async Task Returns_to_the_launched_cover_in_its_library()
    {
        using (var seed = _env.OpenLibrary()) Seed(seed, _env);
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;
        var profile = _env.FakeProfile(sleep: 0.4) with { Generation = Generations.Both };
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);

        vm.SelectedLibraryTab = vm.LibraryTabs[1];
        var target = vm.Items.Single(i => i.Title == "Arcade Archives DONKEY KONG").Id;
        vm.Select(target);
        Assert.True(await vm.PlaySelectedAsync());

        // While the emulator runs: switch library and search for something else.
        vm.SelectedLibraryTab = vm.LibraryTabs[0];
        vm.SearchText = "another";
        await WaitForExit(vm);

        Assert.Equal(Libraries.Arcade, vm.CurrentLibrary);
        Assert.Equal(target, vm.SelectedId);
    }

    [AvaloniaFact]
    public async Task Play_version_launches_the_chosen_file()
    {
        using var lib = _env.OpenLibrary();
        var main = _env.Game("Game [v0].nsp");
        var alt = _env.Game("Game [v131072].nsp");
        var id = lib.AddEdition("Game", Generations.Switch1, main);
        lib.AddFile(id, alt);
        var profile = _env.FakeProfile();
        lib.SaveProfile(profile);
        lib.SetGenerationDefault(Generations.Switch1, profile.Id);
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(id);
        Assert.True(vm.HasVersions);
        Assert.Equal("Versions: 2 files", vm.VersionsText);

        ui.OnVersions = v =>
        {
            Assert.Equal(["Game [v0].nsp", "Game [v131072].nsp"], v.Rows.Select(r => r.FileName));
            Assert.True(v.Rows[0].IsMain);
            Assert.Contains("v131072", v.Rows[1].Detail);
            return v.Rows[1].File.Path;
        };
        await vm.PlayVersionCommand.ExecuteAsync(null);
        await WaitForExit(vm);
        Assert.Equal(alt, _env.ReadRecord().GetProperty("game").GetString());
        Assert.Equal(main, lib.GetEdition(id)!.PrimaryFile!.Path); // one-time: main file unchanged
    }

    [AvaloniaFact]
    public async Task Add_version_file_from_details()
    {
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Game", Generations.Switch1, _env.Game("Game.nsp"));
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi { NextPickedFile = _env.Game("Game (Rev 1).nsp") };
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(id);
        await vm.AddVersionFileCommand.ExecuteAsync(null);
        Assert.Equal(2, lib.GetEdition(id)!.Files.Count);
        Assert.True(vm.HasVersions);

        ui.NextPickedFile = _env.Game("Game (Rev 1).nsp"); // same file again
        await vm.AddVersionFileCommand.ExecuteAsync(null);
        Assert.Contains(ui.Messages, m => m.Contains("already one of this game's versions"));
    }

    [AvaloniaFact]
    public void Versions_dialog_make_main_and_remove()
    {
        using var lib = _env.OpenLibrary();
        var id = lib.AddEdition("Game", Generations.Switch1, _env.Game("A.nsp"));
        lib.AddFile(id, _env.Game("B.nsp"));
        var ui = new FakeUi { ConfirmResult = true };
        var v = new VersionsViewModel(lib.GetEdition(id)!, lib, ui);
        v.Selected = v.Rows[1];
        v.MakeMainCommand.Execute(null);
        Assert.Equal("B.nsp", v.Rows[0].FileName);
        Assert.True(v.Rows[0].IsMain);
        v.Selected = v.Rows[1];
        v.RemoveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Single(v.Rows);
        Assert.False(v.RemoveCommand.CanExecute(null)); // the last file stays
    }

    // ---------------------------------------------------------------- carousel
    [AvaloniaFact]
    public void Carousel_enlarges_and_centres_the_selected_cover()
    {
        using (var seed = _env.OpenLibrary())
        {
            Seed(seed, _env);
            for (var i = 1; i <= 6; i++)
                seed.AddEdition($"Filler Game {i}", Generations.Switch1, _env.Game($"Filler Game {i}.nsp"));
        }
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;
        vm.IsCarousel = true;
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        vm.Select(vm.Items[4].Id);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250); // let the scale transition and glide finish
        Dispatcher.UIThread.RunJobs();
        window.CenterCarousel(animate: false);
        Dispatcher.UIThread.RunJobs();

        var carousel = window.FindControl<ListBox>("CarouselList")!;
        var selected = (Control)carousel.ContainerFromIndex(4)!;
        var neighbour = (Control)carousel.ContainerFromIndex(3)!;
        var selectedWidth = selected.Bounds.Width * ScaleOf(selected);
        var neighbourWidth = neighbour.Bounds.Width * ScaleOf(neighbour);
        var ratio = selectedWidth / neighbourWidth;
        Assert.InRange(ratio, 1.15, 1.20); // ~15–20% larger than its neighbours

        var centre = selected.TranslatePoint(new Avalonia.Point(selected.Bounds.Width / 2, 0), carousel)!.Value.X;
        Assert.InRange(centre, carousel.Bounds.Width / 2 - 2, carousel.Bounds.Width / 2 + 2);
        MainWindowTests.Capture(window, "carousel.png");
        window.Close();
    }

    private static double ScaleOf(Control c) =>
        c.RenderTransform is Avalonia.Media.Transformation.TransformOperations ops ? ops.Value.M11 : 1.0;

    // -------------------------------------------------------- controller input
    [Fact]
    public void Gamepad_buttons_fire_once_and_directions_repeat()
    {
        var m = new GamepadMapper();
        var t = TimeSpan.Zero;
        Assert.Equal([PadAction.Accept], m.Update(new PadState(PadButtons.A), t));
        Assert.Empty(m.Update(new PadState(PadButtons.A), t += TimeSpan.FromMilliseconds(500))); // held: once
        Assert.Empty(m.Update(new PadState(PadButtons.None), t));

        Assert.Equal([PadAction.Right], m.Update(new PadState(PadButtons.DpadRight), t));
        Assert.Empty(m.Update(new PadState(PadButtons.DpadRight), t + TimeSpan.FromMilliseconds(300)));
        Assert.Equal([PadAction.Right], m.Update(new PadState(PadButtons.DpadRight), t + GamepadMapper.RepeatDelay));
        Assert.Equal([PadAction.Right], m.Update(new PadState(PadButtons.DpadRight), t + GamepadMapper.RepeatDelay + GamepadMapper.RepeatInterval));

        Assert.Equal([PadAction.NextLibrary, PadAction.ToggleView],
            m.Update(new PadState(PadButtons.RightShoulder | PadButtons.Y), t += TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Gamepad_stick_has_a_deadzone_and_reset_ignores_held_input()
    {
        var m = new GamepadMapper();
        Assert.Empty(m.Update(new PadState(PadButtons.None, LeftX: 8000), TimeSpan.Zero)); // drift
        Assert.Equal([PadAction.Left], m.Update(new PadState(PadButtons.None, LeftX: -30000), TimeSpan.FromMilliseconds(16)));
        Assert.Equal([PadAction.Up], m.Update(new PadState(PadButtons.None, LeftY: 30000), TimeSpan.FromMilliseconds(32)));

        // Returning from the emulator with A still held must not press Play again.
        m.Reset(new PadState(PadButtons.A | PadButtons.DpadDown));
        Assert.Empty(m.Update(new PadState(PadButtons.A | PadButtons.DpadDown), TimeSpan.FromSeconds(5)));
        Assert.Empty(m.Update(new PadState(PadButtons.A | PadButtons.DpadDown), TimeSpan.FromSeconds(6))); // no repeat either
        Assert.Empty(m.Update(new PadState(PadButtons.None), TimeSpan.FromSeconds(7)));                       // released
        Assert.Equal([PadAction.Accept], m.Update(new PadState(PadButtons.A), TimeSpan.FromSeconds(8)));      // a fresh press works
    }

    [AvaloniaFact]
    public void Gamepad_navigates_the_carousel_and_switches_library_and_view()
    {
        using (var seed = _env.OpenLibrary()) Seed(seed, _env);
        var (lib, vm, _, launcher) = Window();
        using var _l = lib;
        using var _x = launcher;
        vm.IsCarousel = true;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.Select(vm.Items[0].Id);
        Dispatcher.UIThread.RunJobs();
        window.FocusSelectedCover();
        Dispatcher.UIThread.RunJobs();

        GamepadService.Dispatch(window, PadAction.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.Items[1].Id, vm.SelectedId);
        GamepadService.Dispatch(window, PadAction.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.Items[0].Id, vm.SelectedId);

        GamepadService.Dispatch(window, PadAction.NextLibrary);
        Assert.Equal(Libraries.Arcade, vm.CurrentLibrary);
        Assert.False(vm.IsCarousel); // Arcade has its own view setting, grid by default
        GamepadService.Dispatch(window, PadAction.ToggleView);
        Assert.True(vm.IsCarousel);
        window.Close();
    }

    private static async Task WaitForExit(MainViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (vm.IsRunning)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
