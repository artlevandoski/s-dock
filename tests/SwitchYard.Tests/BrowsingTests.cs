using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using SwitchYard.App.ViewModels;
using SwitchYard.App.Views;
using SwitchYard.Core;

namespace SwitchYard.Tests;

/// <summary>Sort titles, SEGA AGES in Arcade Archives, the background setting and a steady carousel.</summary>
public sealed class BrowsingTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    // ------------------------------------------------------------- sort title
    [Fact]
    public void Sort_title_orders_games_and_persists()
    {
        string botw;
        using (var lib = _env.OpenLibrary())
        {
            lib.AddEdition("Animal Crossing", Generations.Switch1, _env.Game("AC.nsp"));
            botw = lib.AddEdition("The Legend of Zelda Breath of the Wild", Generations.Switch1, _env.Game("BOTW.nsp"));
            lib.AddEdition("Mario Kart 8", Generations.Switch1, _env.Game("MK8.nsp"));
            Assert.Equal("The Legend of Zelda Breath of the Wild", lib.ListEditions()[^1].Title);
            lib.SetSortTitle(botw, "  Legend of Zelda 1 ");
        }
        using (var lib = _env.OpenLibrary())
        {
            Assert.Equal(["Animal Crossing", "The Legend of Zelda Breath of the Wild", "Mario Kart 8"], lib.ListEditions().Select(e => e.Title));
            Assert.Equal("Legend of Zelda 1", lib.GetEdition(botw)!.SortTitle);
            lib.SetSortTitle(botw, " ");
            Assert.Null(lib.GetEdition(botw)!.SortTitle);
        }
    }

    [AvaloniaFact]
    public async Task Sort_title_from_game_actions()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Bravo", Generations.Switch1, _env.Game("B.nsp"));
        var zed = lib.AddEdition("Zed", Generations.Switch1, _env.Game("Z.nsp"));
        using var launcher = new EmulatorLauncher();
        var ui = new FakeUi();
        var vm = new MainViewModel(lib, launcher, ui);
        vm.Select(zed);
        ui.OnEditText = (_, initial) => { Assert.Equal("Zed", initial); return "Aardvark\n"; };
        await vm.SetSortTitleCommand.ExecuteAsync(null);
        Assert.Equal(["Zed", "Bravo"], vm.Items.Select(i => i.Title));
        Assert.Equal("Zed", vm.SelectedItem?.Title);
        Assert.Contains("Sorted as: Aardvark", vm.DetailsText);

        ui.OnEditText = (_, _) => "Zed"; // back to the title: cleared
        await vm.SetSortTitleCommand.ExecuteAsync(null);
        Assert.Null(lib.GetEdition(zed)!.SortTitle);
    }

    [Fact]
    public void Upgrade_from_v7_adds_sort_title()
    {
        using (var lib = _env.OpenLibrary()) lib.AddEdition("Kept", Generations.Switch1, _env.Game());
        using (var conn = new SqliteConnection($"Data Source={_env.DbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE editions DROP COLUMN sort_title; UPDATE meta SET value = '7' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }
        using var upgraded = _env.OpenLibrary();
        Assert.Equal(LibraryStore.SchemaVersion, upgraded.StoredSchemaVersion);
        var id = upgraded.ListEditions().Single().Id;
        upgraded.SetSortTitle(id, "K");
        Assert.Equal("K", upgraded.GetEdition(id)!.SortTitle);
    }

    // ------------------------------------------------------------- SEGA AGES
    [Theory]
    [InlineData("SEGA AGES Sonic The Hedgehog", ArcadeSeries.SegaAges)]
    [InlineData("Sega_Ages Out Run", ArcadeSeries.SegaAges)]
    [InlineData("Sega Agesque", ArcadeSeries.None)]
    [InlineData("Sonic Mania", ArcadeSeries.None)]
    public void Sega_ages_by_title_prefix(string title, string expected) => Assert.Equal(expected, ArcadeSeries.Detect(title));

    [AvaloniaFact]
    public void Sega_ages_join_arcade_archives_when_ticked()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("SEGA AGES Out Run", Generations.Switch1, _env.Game("OutRun.nsp"));
        lib.AddEdition("Arcade Archives DONKEY KONG", Generations.Switch1, _env.Game("DK.nsp"));
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        Assert.Equal(1, vm.LibraryTabs.First(t => t.Id == Libraries.Main).Count);
        Assert.DoesNotContain(vm.SeriesFilterOptions, o => o.Value == ArcadeSeries.SegaAges);

        vm.IncludeSegaAges = true;
        Assert.Equal(0, vm.LibraryTabs.First(t => t.Id == Libraries.Main).Count);
        Assert.Equal(2, vm.LibraryTabs.First(t => t.Id == Libraries.Arcade).Count);
        vm.SelectedLibraryTab = vm.LibraryTabs.First(t => t.Id == Libraries.Arcade);
        vm.SeriesFilter = vm.SeriesFilterOptions.First(o => o.Value == ArcadeSeries.SegaAges);
        Assert.Equal(["SEGA AGES Out Run"], vm.Items.Select(i => i.Title));
        Assert.True(new MainViewModel(lib, launcher, new FakeUi()).IncludeSegaAges); // remembered

        vm.IncludeSegaAges = false;
        Assert.Equal(FilterOption.AllValue, vm.SeriesFilter.Value); // its series filter went with it
    }

    // ------------------------------------------------------------- background
    private static Bitmap Solid(Color color)
    {
        var bmp = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        var row = new byte[fb.RowBytes];
        for (var x = 0; x < 32; x++)
        {
            row[x * 4] = color.B; row[x * 4 + 1] = color.G; row[x * 4 + 2] = color.R; row[x * 4 + 3] = 255;
        }
        for (var y = 0; y < 32; y++)
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, fb.RowBytes);
        return bmp;
    }

    [AvaloniaFact]
    public void Cover_colour_follows_the_artwork()
    {
        var blue = CoverColors.Pick(Solid(Color.FromRgb(20, 60, 230)))!.Value;
        Assert.True(blue.B > blue.R && blue.B > blue.G, $"{blue}");
        var orange = CoverColors.Pick(Solid(Color.FromRgb(240, 130, 10)))!.Value;
        Assert.True(orange.R > orange.G && orange.G > orange.B, $"{orange}");
        var grey = CoverColors.Pick(Solid(Color.FromRgb(250, 250, 250)))!.Value;
        Assert.InRange(grey.R, 90, 170); // toned down so white text stays readable
    }

    [AvaloniaFact]
    public void Background_setting_switches_the_backdrop_and_is_remembered()
    {
        using var lib = _env.OpenLibrary();
        lib.AddEdition("Haunted House", Generations.Switch1, _env.Game());
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        Assert.True(vm.BackgroundIsArt);
        Assert.NotNull(vm.BackdropBrush);

        vm.BackgroundIsColor = true;
        Assert.False(vm.BackgroundIsArt);
        var gradient = Assert.IsType<LinearGradientBrush>(vm.BackdropBrush);
        Assert.Equal(vm.SelectedColor, gradient.GradientStops[0].Color);

        vm.BackgroundIsPlain = true;
        Assert.Null(vm.BackdropBrush);
        Assert.Equal(MainViewModel.BackgroundPlain, new MainViewModel(lib, launcher, new FakeUi()).BackgroundStyle);
    }

    // ------------------------------------------------------------- steady carousel
    [AvaloniaFact]
    public void Carousel_covers_and_details_stay_put_while_browsing()
    {
        using var lib = _env.OpenLibrary();
        var shortOne = lib.AddEdition("A", Generations.Switch1, _env.Game("A.nsp"));
        var longOne = lib.AddEdition("B", Generations.Switch1, _env.Game("B.nsp"));
        lib.SaveMetadata(SwitchYard.Core.Metadata.EditionMetadata.Empty(longOne) with
        {
            UserSummary = string.Join(" ", Enumerable.Repeat("A very long description that wraps over several lines.", 20)),
        });
        using var launcher = new EmulatorLauncher();
        var vm = new MainViewModel(lib, launcher, new FakeUi()) { IsCarousel = true };
        var window = new MainWindow { DataContext = vm, Width = 1600, Height = 1000 };
        window.Show();

        (Rect Band, Rect List, List<Size> Covers) Measure(string id)
        {
            vm.Select(id);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var band = window.FindControl<Border>("CarouselInfoBand")!;
            var list = window.FindControl<ListBox>("CarouselList")!;
            var covers = list.GetVisualDescendants().OfType<SwitchYard.App.Controls.CoverView>().Select(c => c.Bounds.Size).ToList();
            return (band.Bounds, list.Bounds, covers);
        }

        var a = Measure(shortOne);
        var b = Measure(longOne);
        Assert.Equal(a.Band, b.Band);
        Assert.Equal(a.List, b.List);
        Assert.All(a.Covers.Concat(b.Covers), s => Assert.Equal(new Size(300, 300), s));
        window.Close();
    }
}
