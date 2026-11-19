using Avalonia.Headless.XUnit;
using SDock.App.ViewModels;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>Remove from Library: the game and its media leave S-Dock; the game file stays on the PC.</summary>
public sealed class RemoveGameTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [AvaloniaFact]
    public async Task Removes_the_game_its_settings_and_artwork_but_never_the_file()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var bravoFile = _env.Game("B.nsp");
        var bravo = lib.AddEdition("Bravo", Generations.Switch1, bravoFile);
        lib.AddEdition("Charlie", Generations.Switch1, _env.Game("C.nsp"));
        lib.SetFavorite(bravo, true);
        lib.SetLaunchOverride(bravo, LaunchRouter.NoEmulatorId);
        lib.SetEmuReady(bravo, EmuReadySummary.NotFound(DateTime.UtcNow));
        UserMetadata.SetArtBytes(lib, AppPaths.ArtworkDir, bravo, ArtKind.Logo, [1, 2, 3], ".png");
        var art = Path.Combine(AppPaths.ArtworkDir, bravo);
        Assert.True(Directory.Exists(art));

        var ui = new FakeUi { ChooseButton = "Remove" };
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        vm.Select(bravo);
        vm.OpenGameMenu();
        Assert.Contains(vm.MenuItems, m => m.Label == "Remove from Library…");
        vm.CloseMenu();

        await vm.RemoveFromLibraryCommand.ExecuteAsync(null);
        Assert.Contains("isn't touched", ui.Messages.Last());
        Assert.Null(lib.GetEdition(bravo));
        Assert.Equal(["Alpha", "Charlie"], vm.Items.Select(i => i.Title));
        Assert.Equal("Charlie", vm.SelectedTitle);                   // the neighbour, not the first game
        Assert.DoesNotContain(bravo, lib.FavoriteEditions());
        Assert.False(lib.AllEmuReady().ContainsKey(bravo));
        Assert.Null(lib.GetSetting($"no_emulator:edition:{bravo}"));
        Assert.False(Directory.Exists(art));                         // its media is gone
        Assert.True(File.Exists(bravoFile));                         // the game file is not
    }

    [AvaloniaFact]
    public async Task Cancel_keeps_everything()
    {
        using var lib = _env.OpenLibrary();
        var alpha = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var ui = new FakeUi { ChooseButton = null };                 // the question is closed
        var vm = new MainViewModel(lib, new EmulatorLauncher(), ui);
        await vm.RemoveFromLibraryCommand.ExecuteAsync(null);
        Assert.NotNull(lib.GetEdition(alpha));
        Assert.Single(vm.Items);
    }
}

