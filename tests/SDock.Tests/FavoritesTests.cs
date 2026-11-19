using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Favorites: marked in either view, kept in the database, and gathered in their own tab.</summary>
public sealed class FavoritesTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private (MainViewModel Vm, MainWindow Window) Open(LibraryStore lib, bool carousel)
    {
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { IsCarousel = carousel };
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.FocusSelectedCover();
        return (vm, window);
    }

    private static void Press(MainWindow window, PadAction action)
    {
        GamepadService.Dispatch(window, action);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Favorites_are_stored_and_go_with_the_game()
    {
        using var lib = _env.OpenLibrary();
        var a = lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        Assert.Empty(lib.FavoriteEditions());
        lib.SetFavorite(a, true);
        lib.SetFavorite(a, true); // twice is fine
        Assert.Equal([a], lib.FavoriteEditions());
        lib.SetFavorite(a, false);
        Assert.Empty(lib.FavoriteEditions());
    }

    [AvaloniaFact]
    public void Carousel_marks_a_favorite_and_the_tab_gathers_them_from_every_library()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var aca = lib.AddEdition("Arcade Archives Galaga", Generations.Switch1, _env.Game("G.nsp"));
        lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        var (vm, window) = Open(lib, carousel: true);
        Assert.Equal(["Main Library", "Arcade Archives", "Favorites", "Recently Played"], vm.LibraryTabs.Select(t => t.Label));

        vm.Select(vm.Items.Single(i => i.Title == "Bravo").Id);
        Assert.Equal("☆", vm.FavoriteGlyph);
        Press(window, PadAction.GameActions);                      // X: Game Actions
        vm.ActivateMenu(vm.MenuItems.Single(m => m.Label == "Add to Favorites"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.SelectedItem!.IsFavorite);
        Assert.Equal("Bravo", vm.SelectedTitle);                   // still on the same game
        Assert.Equal("★", vm.FavoriteGlyph);
        lib.SetFavorite(aca, true);
        vm.Refresh();

        Press(window, PadAction.NextLibrary);
        Press(window, PadAction.NextLibrary);                      // RB twice: Favorites
        Assert.True(vm.IsFavoritesTab);
        Assert.Equal(["Arcade Archives Galaga", "Bravo"], vm.Items.Select(i => i.Title));
        Assert.Equal(2, vm.LibraryTabs.Single(t => t.Id == Libraries.Favorites).Count);
        Assert.Equal(2, vm.LibraryTabs.Single(t => t.Id == Libraries.Main).Count);   // Main still has both of its games

        // Unmarked in Favorites: it leaves the tab and the selection moves to a neighbour.
        vm.Select(vm.Items.Single(i => i.Title == "Bravo").Id);
        window.FindControl<Button>("CarouselFavoriteButton")!.Command!.Execute(null); // the ★ button
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Arcade Archives Galaga"], vm.Items.Select(i => i.Title));
        Assert.Equal("Arcade Archives Galaga", vm.SelectedTitle);
        Assert.Equal([aca], lib.FavoriteEditions());
        window.Close();
    }

    [AvaloniaFact]
    public void Grid_marks_with_the_star_button_or_game_actions_and_covers_show_a_star()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var (vm, window) = Open(lib, carousel: false);

        window.FindControl<Button>("FavoriteButton")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.SelectedIsFavorite);
        window.UpdateLayout();
        var star = window.FindControl<ListBox>("CoverList")!.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("favstar"));
        Assert.True(star.IsEffectivelyVisible);

        Press(window, PadAction.GameActions);                      // X: Game Actions in the grid
        Assert.True(vm.IsMenuOpen);
        var entry = vm.MenuItems.Single(m => m.Label == "Remove from Favorites");
        vm.ActivateMenu(entry);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.SelectedIsFavorite);
        Assert.Equal("Add to Favorites", vm.FavoriteMenuLabel);
        window.Close();
    }

    [AvaloniaFact]
    public void An_empty_favorites_tab_says_how_to_add_one()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Alpha", Generations.Switch1, _env.Game("A.nsp"));
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi());
        vm.SelectedLibraryTab = vm.LibraryTabs.Single(t => t.Id == Libraries.Favorites);
        Assert.True(vm.ShowEmpty);
        Assert.StartsWith("No favorites yet.", vm.EmptyText);
    }
}
