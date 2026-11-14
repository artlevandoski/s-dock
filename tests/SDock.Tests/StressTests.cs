using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using SDock.App.ViewModels;
using SDock.App.Views;
using SDock.Core;
using SDock.Core.Metadata;

namespace SDock.Tests;

/// <summary>
/// Performance check with a library shaped like a real one: 863 folders, each holding a base game,
/// an update and DLC, with IGDB-sized cover art. Runs only when SDOCK_STRESS=1:
///   $env:SDOCK_STRESS=1; dotnet test -c Release --filter "FullyQualifiedName~StressTests"
/// The generated files live in %TEMP%\S-Dock Stress and are reused between runs.
/// </summary>
public sealed class StressTests(ITestOutputHelper output)
{
    private const int Games = 863;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "S-Dock Stress");
    private static string GamesDir => Path.Combine(Root, "games");
    private static string ArtDir => Path.Combine(Root, "art");

    private static readonly string[] Adjectives =
        ["Crimson", "Silent", "Glass", "Iron", "Hidden", "Golden", "Frozen", "Electric", "Ancient", "Midnight", "Lost", "Wild",
         "Hollow", "Star", "Shadow", "Neon", "Emerald", "Rogue", "Tiny", "Mighty", "Sunken", "Paper", "Thunder", "Lunar"];
    private static readonly string[] Nouns =
        ["Harbor", "Express", "Quest", "Kingdom", "Racer", "Legends", "Tactics", "Garden", "Odyssey", "Dungeon", "Circuit",
         "Frontier", "Saga", "Party", "Arena", "Islands", "Chronicles", "Rush", "Tales", "Station", "Engine", "Valley"];

    private static string TitleFor(int i) =>
        $"{Adjectives[i % Adjectives.Length]} {Nouns[(i / Adjectives.Length) % Nouns.Length]}" +
        (i >= Adjectives.Length * Nouns.Length ? $" {i / (Adjectives.Length * Nouns.Length) + 1}" : "");

    private static bool Enabled => Environment.GetEnvironmentVariable("SDOCK_STRESS") == "1";

    /// <summary>863 folders × (base + update + 1–2 DLC), and a cover JPEG per game.</summary>
    private static void EnsureFiles()
    {
        if (File.Exists(Path.Combine(Root, "ready.txt"))) return;
        Directory.CreateDirectory(GamesDir);
        Directory.CreateDirectory(ArtDir);
        var rng = new Random(863);
        for (var i = 0; i < Games; i++)
        {
            var title = TitleFor(i);
            var tid = $"0100{i:X4}{rng.Next(0x1000):X3}0000";
            tid = tid[..13] + "000";
            var dir = Path.Combine(GamesDir, title);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{title} [{tid}][v0].nsp"), "base");
            File.WriteAllText(Path.Combine(dir, $"{title} [{tid[..13]}800][v{65536 * (1 + i % 5)}].nsp"), "update");
            File.WriteAllText(Path.Combine(dir, $"{title} Expansion Pass [{tid[..12]}1001][v0].nsp"), "dlc");
            if (i % 3 == 0)
                File.WriteAllText(Path.Combine(dir, $"{title} Bonus Costumes [{tid[..12]}1002][v0].nsp"), "dlc");
            WriteCover(Path.Combine(ArtDir, $"{i:D4}.jpg"), i, rng);
        }
        File.WriteAllText(Path.Combine(Root, "ready.txt"), DateTime.Now.ToString("O"));
    }

    /// <summary>A 528×748 JPEG with gradient, shapes and noise, so it's about the size and weight of an IGDB cover.</summary>
    private static void WriteCover(string path, int i, Random rng)
    {
        using var surface = SKSurface.Create(new SKImageInfo(528, 748));
        var c = surface.Canvas;
        var hue = i * 37 % 360;
        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(528, 748),
                [SKColor.FromHsv(hue, 60, 90), SKColor.FromHsv((hue + 60) % 360, 80, 30)], SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, 528, 748, paint);
        }
        using (var paint = new SKPaint { IsAntialias = true })
        {
            for (var k = 0; k < 40; k++)
            {
                paint.Color = SKColor.FromHsv(rng.Next(360), 50, 100).WithAlpha((byte)rng.Next(40, 120));
                c.DrawCircle(rng.Next(528), rng.Next(748), rng.Next(10, 120), paint);
            }
        }
        using (var paint = new SKPaint())
        {
            for (var k = 0; k < 9000; k++) // film grain: keeps the JPEG realistically "busy"
            {
                paint.Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 60);
                c.DrawPoint(rng.Next(528), rng.Next(748), paint);
            }
        }
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, 88);
        using var f = File.Create(path);
        data.SaveTo(f);
    }

    private void Log(string what, Stopwatch sw) => output.WriteLine($"{what,-46} {sw.ElapsedMilliseconds,6} ms");

    private static void Pump(int ms = 0)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (DateTime.UtcNow < until);
    }

    [AvaloniaFact]
    public async Task Large_library_timings()
    {
        if (!Enabled) Assert.Skip("Set SDOCK_STRESS=1 to run the large-library stress test.");
        var sw = Stopwatch.StartNew();
        EnsureFiles();
        Log("Generate files (first run only)", sw);

        // Keep this run's database and artwork together, so the real app can open it for --benchmark:
        //   S-Dock.exe --data-dir "%TEMP%/S-Dock Stress/run" --benchmark
        var runDir = Path.Combine(Root, "run");
        if (Directory.Exists(runDir)) Directory.Delete(runDir, true);
        var previousDataDir = Environment.GetEnvironmentVariable(AppPaths.DataDirEnv);
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, runDir);
        try { await RunAsync(runDir); }
        finally { Environment.SetEnvironmentVariable(AppPaths.DataDirEnv, previousDataDir); }
    }

    private async Task RunAsync(string runDir)
    {
        var sw = Stopwatch.StartNew();
        var db = Path.Combine(runDir, "library.sqlite3");
        using var lib = new LibraryStore(db);

        sw.Restart();
        var scan = LibraryScanner.ScanFolder(GamesDir, Generations.Switch1, lib);
        Log($"Scan ({scan.Entries.Count} files)", sw);
        Assert.Equal(Games, scan.Entries.Count(e => e.Include));

        sw.Restart();
        var preview = new ImportPreviewWindow(new ImportPreviewViewModel(scan, Generations.Switch1)) { Width = 1180, Height = 700 };
        preview.Show();
        Pump();
        _ = preview.CaptureRenderedFrame();
        Log("Import preview: open and first render", sw);
        preview.Close();

        sw.Restart();
        var summary = LibraryScanner.ImportEntries(lib, GamesDir, Generations.Switch1, scan.Entries);
        Log($"Import ({summary.Added.Count} games)", sw);

        // Give every game a cover, as after Match Unmatched Games.
        var artworkDir = AppPaths.ArtworkDir;
        var editions = lib.ListEditions();
        sw.Restart();
        for (var i = 0; i < editions.Count; i++)
        {
            var dir = Path.Combine(artworkDir, editions[i].Id);
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(ArtDir, $"{i:D4}.jpg"), Path.Combine(dir, "cover.jpg"), overwrite: true);
            lib.SaveMetadata(EditionMetadata.Empty(editions[i].Id) with { CoverFile = Path.Combine(editions[i].Id, "cover.jpg") });
            lib.SetIgdbId(editions[i].Id, 100000 + i);
        }
        Log("Attach covers", sw);

        var before = GC.GetTotalMemory(true);
        using var launcher = new EmulatorLauncher();
        sw.Restart();
        var vm = new MainViewModel(lib, launcher, new FakeUi());
        Log("Main view model (load library)", sw);

        sw.Restart();
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 820 };
        window.Show();
        Pump();
        _ = window.CaptureRenderedFrame();
        Log("Main window: open and first frame (grid)", sw);

        sw.Restart();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var firstVisible = false;
        while (DateTime.UtcNow < deadline)
        {
            Pump(10);
            if (!firstVisible && vm.Items.Take(30).All(i => i.CoverReady))
            {
                firstVisible = true;
                Log("First screen of covers ready", sw);
            }
            if (SDock.App.Controls.ImageCache.Covers.Pending == 0 && vm.Items.All(i => i.CoverReady)) break;
        }
        Log("All covers ready in memory", sw);
        output.WriteLine($"{"Cover cache",-46} {SDock.App.Controls.ImageCache.Covers.Count,6} images, {SDock.App.Controls.ImageCache.Covers.Bytes / 1048576} MB at {SDock.App.Controls.ImageCache.Covers.DecodeWidth} px");
        output.WriteLine($"{"Managed memory after covers",-46} {(GC.GetTotalMemory(false) - before) / 1048576,6} MB");
        output.WriteLine($"{"Process working set",-46} {Process.GetCurrentProcess().WorkingSet64 / 1048576,6} MB");

        var grid = window.FindControl<ListBox>("CoverList")!;
        sw.Restart();
        var scroller = grid.FindDescendantOfType<ScrollViewer>()!;
        for (var step = 0; step < 40; step++)
        {
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + 600);
            Dispatcher.UIThread.RunJobs();
            _ = window.CaptureRenderedFrame();
        }
        Log("Scroll grid 40 pages (render each)", sw);

        sw.Restart();
        vm.GenerationFilter = FilterOption.Generation.Single(o => o.Value == Generations.Switch2);
        vm.GenerationFilter = FilterOption.Generation[0];
        Pump();
        Log("Filter change and back", sw);

        sw.Restart();
        vm.Refresh();
        Pump();
        _ = window.CaptureRenderedFrame();
        Log("Full refresh", sw);

        sw.Restart();
        vm.IsCarousel = true;
        Pump(50);
        _ = window.CaptureRenderedFrame();
        Log("Switch to carousel", sw);

        var carousel = window.FindControl<ListBox>("CarouselList")!;
        sw.Restart();
        for (var step = 0; step < 60; step++) vm.SelectedItem = vm.Items[200 + step];
        Log("  (view model only: 60 selection changes)", sw);
        sw.Restart();
        for (var step = 0; step < 60; step++) { vm.SelectedItem = vm.Items[300 + step]; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Log("  (with layout, no rendering: 60 steps)", sw);
        sw.Restart();
        for (var step = 0; step < 60; step++)
        {
            carousel.SelectedIndex = Math.Min(carousel.ItemCount - 1, carousel.SelectedIndex + 1);
            Dispatcher.UIThread.RunJobs();
            _ = window.CaptureRenderedFrame();
        }
        Log("Carousel: 60 steps right (render each)", sw);

        // Diagnostic: the same steps with the selected cover's glow (box shadow) turned off.
        var noGlow = new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Descendant().OfType<Border>().Class("ring"));
        noGlow.Setters.Add(new Avalonia.Styling.Setter(Border.BoxShadowProperty, new Avalonia.Media.BoxShadows()));
        window.Styles.Add(noGlow);
        sw.Restart();
        for (var step = 0; step < 60; step++)
        {
            carousel.SelectedIndex = Math.Max(0, carousel.SelectedIndex - 1);
            Dispatcher.UIThread.RunJobs();
            _ = window.CaptureRenderedFrame();
        }
        Log("  (same, glow off: 60 steps)", sw);
        window.Styles.Remove(noGlow);
        window.Close();
        await Task.CompletedTask;
    }
}

internal static class VisualHelpers
{
    public static T? FindDescendantOfType<T>(this Avalonia.Visual v) where T : class =>
        Avalonia.VisualTree.VisualExtensions.FindDescendantOfType<T>(v);
}
