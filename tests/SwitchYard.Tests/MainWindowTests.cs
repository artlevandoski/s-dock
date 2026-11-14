using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

[assembly: AvaloniaTestApplication(typeof(SwitchYard.Tests.TestAppBuilder))]

namespace SwitchYard.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SwitchYard.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Fake dialogs: accepts previews, records messages, never blocks.</summary>
public sealed class FakeUi : IUiServices
{
    public List<string> Messages { get; } = [];
    public int FocusRestores { get; private set; }
    public bool AcceptImport { get; set; } = true;

    public Task<string?> PickFolderAsync(string title, string? startDir = null) => Task.FromResult<string?>(null);
    public Task<string?> PickFileAsync(string title, string? startDir, string filterName, params string[] patterns)
    {
        var f = NextPickedFile;
        NextPickedFile = null;
        return Task.FromResult(f);
    }
    public Task ShowMessageAsync(string title, string message) { Messages.Add(message); return Task.CompletedTask; }
    public bool ConfirmResult { get; set; }
    public Task<bool> ConfirmAsync(string title, string message) { Messages.Add(message); return Task.FromResult(ConfirmResult); }
    public Task<string?> ChooseAsync(string title, string heading, string message, params string[] buttons)
    { Messages.Add(heading + "\n" + message); return Task.FromResult<string?>(null); }
    public Task<bool> ShowImportPreviewAsync(ImportPreviewViewModel vm) => Task.FromResult(AcceptImport);
    public Task<bool> ShowEmulatorSettingsAsync(EmulatorProfilesViewModel vm) => Task.FromResult(false);

    /// <summary>Pickers shown, in order. <see cref="OnPick"/> decides the answer (default: cancel).</summary>
    public List<ProfilePickerViewModel> Pickers { get; } = [];
    public Func<ProfilePickerViewModel, bool> OnPick { get; set; } = _ => false;
    public Task<bool> ShowProfilePickerAsync(ProfilePickerViewModel vm) { Pickers.Add(vm); return Task.FromResult(OnPick(vm)); }

    /// <summary>Play Version…: <see cref="OnVersions"/> decides which file to play (default: close).</summary>
    public Func<VersionsViewModel, string?> OnVersions { get; set; } = _ => null;
    public Task<string?> ShowVersionsAsync(VersionsViewModel vm) => Task.FromResult(OnVersions(vm));

    public string? NextPickedFile { get; set; }

    public Func<IgdbSettingsViewModel, bool> OnIgdbSettings { get; set; } = _ => false;
    public Task<bool> ShowIgdbSettingsAsync(IgdbSettingsViewModel vm) => Task.FromResult(OnIgdbSettings(vm));

    /// <summary>Match on IGDB…: runs the dialog's first search, then lets the test choose.</summary>
    public Func<MatchViewModel, SwitchYard.Core.Metadata.IgdbGame?> OnMatch { get; set; } = _ => null;
    public async Task<SwitchYard.Core.Metadata.IgdbGame?> ShowMatchAsync(MatchViewModel vm)
    {
        await vm.StartAsync();
        return OnMatch(vm);
    }

    /// <summary>Choose Background… / Choose Logo…: loads the candidates, then lets the test pick.</summary>
    public Func<ArtPickerViewModel, ArtPick?> OnArtPicker { get; set; } = _ => null;
    public async Task<ArtPick?> ShowArtPickerAsync(ArtPickerViewModel vm)
    {
        await vm.StartAsync();
        return OnArtPicker(vm);
    }

    public string? NextEditedText { get; set; }
    public Task<string?> EditTextAsync(string title, string explanation, string initial) => Task.FromResult(NextEditedText);

    public List<string> OpenedUrls { get; } = [];
    public void OpenUrl(string url) => OpenedUrls.Add(url);

    public void OpenFolder(string path) { }
    public void RestoreFocus() => FocusRestores++;
}

public sealed class MainWindowTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private static void PumpUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }

    [AvaloniaFact]
    public async Task Import_restart_play_and_return_to_cover()
    {
        var folder = _env.CreateSampleFolder();
        string targetId;

        // Session 1: import through the view model and configure the fake emulator.
        using (var lib = _env.OpenLibrary())
        using (var launcher = new EmulatorLauncher())
        {
            var vm = new MainViewModel(lib, launcher, new FakeUi());
            Assert.False(vm.HasItems);
            await vm.ImportFolderAsync(folder);
            Assert.Equal(4, vm.Items.Count); // Main Library; the Arcade Archives title is in its own tab
            Assert.Equal([4, 1], vm.LibraryTabs.Select(t => t.Count));
            var profile = _env.FakeProfile(sleep: 0.3);
            lib.SaveProfile(profile);
            lib.SetGenerationDefault(Generations.Switch1, profile.Id);
            // The folder starts Unclassified, so give the target a generation the profile supports.
            targetId = vm.Items.Single(i => i.Title == "Super Placeholder Quest").Id;
            vm.Select(targetId);
            vm.SelectedGenerationOption = GenerationOption.ForLibrary(Generations.Switch1);
        }

        // Session 2: restart restores library, profile and selection.
        using (var lib = _env.OpenLibrary())
        using (var launcher = new EmulatorLauncher())
        {
            var ui = new FakeUi();
            var vm = new MainViewModel(lib, launcher, ui);
            Assert.Equal(4, vm.Items.Count);
            Assert.Equal(targetId, vm.SelectedId);
            Assert.Equal("Fake Eden", Assert.Single(lib.ListProfiles()).Name);

            Assert.True(await vm.PlaySelectedAsync());
            Assert.False(vm.CanPlay);
            Assert.Equal("Running…", vm.PlayLabel);

            // The user wanders to another cover while the emulator runs.
            vm.Select(vm.Items.First(i => i.Id != targetId).Id);
            PumpUntil(() => !vm.IsRunning);

            Assert.Equal(targetId, vm.SelectedId);
            Assert.True(vm.CanPlay);
            Assert.Equal("Play", vm.PlayLabel);
            Assert.True(ui.FocusRestores > 0);
            Assert.Empty(ui.Messages);
            Assert.Empty(ui.Pickers); // the Switch 1 default was used without asking
        }
    }

    [AvaloniaFact]
    public async Task Missing_file_shown_and_app_stays_usable()
    {
        var folder = _env.CreateSampleFolder();
        using var lib = _env.OpenLibrary();
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, launcher, ui);
        await vm.ImportFolderAsync(folder);
        var gone = vm.Items.Single(i => i.Title == "Super Placeholder Quest");
        File.Delete(gone.Edition.PrimaryFile!.Path);
        vm.Refresh(gone.Id);

        Assert.True(vm.SelectedIsMissing);
        Assert.True(vm.ShowMissing);
        Assert.Contains("with missing files", vm.StatusText);

        lib.SaveProfile(_env.FakeProfile());
        ui.OnPick = _ => true; // Unclassified game, no default: Play asks; pick the only profile
        Assert.False(await vm.PlaySelectedAsync());
        Assert.Contains(ui.Messages, m => m.Contains("Game file not found"));
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task Play_without_profile_offers_settings()
    {
        var folder = _env.CreateSampleFolder();
        using var lib = _env.OpenLibrary();
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, launcher, ui);
        await vm.ImportFolderAsync(folder);
        Assert.False(await vm.PlaySelectedAsync());
        Assert.Contains(ui.Messages, m => m.Contains("No emulator is set up yet"));
    }

    [AvaloniaFact]
    public async Task Windows_render_without_errors()
    {
        var folder = _env.CreateSampleFolder();
        using var lib = _env.OpenLibrary();
        using var launcher = new EmulatorLauncher();
        var stable = EmulatorProfile.New("Eden 0.2.1", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        var nightly = EmulatorProfile.New("Eden nightly", TestEnv.FakeEmulatorExe, LaunchArgs.EdenPreset, emulator: "eden");
        lib.SaveProfile(stable);
        lib.SaveProfile(nightly);
        lib.SetGenerationDefault(Generations.Switch1, stable.Id);

        var window = new MainWindow();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        window.DataContext = vm;
        window.Show();
        await vm.ImportFolderAsync(folder);
        vm.Select(vm.Items.Single(i => i.Title == "Super Placeholder Quest").Id);
        vm.SelectedGenerationOption = GenerationOption.ForLibrary(Generations.Switch1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Plays with Eden 0.2.1 (Switch 1 default)", vm.RouteText);
        Assert.Equal("Use default (Eden 0.2.1)", vm.EmulatorChoices[0].Label);
        var covers = window.FindControl<ListBox>("CoverList")!;
        Assert.True(covers.IsVisible);
        Assert.Equal(4, covers.ItemCount);
        Capture(window, "main.png");

        var preview = new ImportPreviewWindow(new ImportPreviewViewModel(
            LibraryScanner.ScanFolder(folder, Generations.Switch1, lib), Generations.Switch1));
        preview.Show();
        Capture(preview, "preview.png");
        preview.Close();

        var emulatorsDir = _env.CreateEmulatorsFolder();
        var settingsVm = new EmulatorProfilesViewModel(lib.ListProfiles(), stable.Id, null, launcher, new FakeUi(), null, emulatorsDir);
        var settings = new EmulatorSettingsWindow(settingsVm);
        settings.Show();
        Capture(settings, "settings.png");
        settings.Close();
        settingsVm.Detach();

        var picker = new ProfilePickerWindow(new ProfilePickerViewModel(vm.SelectedItem!.Edition, lib.ListProfiles(), oneTime: false));
        picker.Show();
        Capture(picker, "picker.png");
        picker.Close();
        window.Close();
    }

    /// <summary>Renders a frame; saves it as PNG when SWITCHYARD_SCREENSHOTS names a folder.</summary>
    internal static void Capture(Window w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SWITCHYARD_SCREENSHOTS");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
#pragma warning disable CS0618 // simple PNG save is fine for test screenshots
            frame.Save(Path.Combine(dir, name));
#pragma warning restore CS0618
        }
    }
}
