using System.Security.Cryptography;
using SwitchYard.Core;

namespace SwitchYard.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private static Dictionary<string, ScanEntry> ByName(ScanResult r) => r.Entries.ToDictionary(e => e.FileName);

    [Fact]
    public void Preview_classifies_and_flags_entries()
    {
        var result = LibraryScanner.ScanFolder(_env.CreateSampleFolder(), Generations.Switch1);
        var e = ByName(result);
        Assert.Equal(8, e.Count); // notes.txt ignored

        var baseGame = e["Super Placeholder Quest [0100AAAA00000000][v0].nsp"];
        Assert.Equal(ContentKinds.Base, baseGame.Kind);
        Assert.Equal("Super Placeholder Quest", baseGame.Title);
        Assert.Equal("0100AAAA00000000", baseGame.TitleId);
        Assert.Equal(Generations.Switch1, baseGame.Generation);
        Assert.True(baseGame.Include);
        Assert.Empty(baseGame.Flags);

        var upd = e["Super Placeholder Quest [0100AAAA00000800][v131072].nsp"];
        Assert.Equal(ContentKinds.Update, upd.Kind);
        Assert.Contains(ScanFlags.Patch, upd.Flags);
        Assert.False(upd.Include);

        var dlc = e["Super Placeholder Quest Bonus Pack [0100AAAA00001001][v0].nsp"];
        Assert.Equal(ContentKinds.Dlc, dlc.Kind);
        Assert.Contains(ScanFlags.Dlc, dlc.Flags);
        Assert.False(dlc.Include);

        var aca = e["Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp"];
        Assert.Equal("Arcade Archives SAMPLE BLASTER", aca.Title);
        Assert.True(aca.Include);

        // Exactly one of the pair is flagged; whichever is seen second.
        ScanEntry[] pair = [e["Another Test Game (copy).xci"], e["Another Test Game.xci"]];
        Assert.Single(pair, x => x.Flags.Contains(ScanFlags.Duplicate));
        Assert.All(pair, x => Assert.Equal(!x.Flags.Contains(ScanFlags.Duplicate), x.Include));

        var ambiguous = e["0100CCCC00000000.nsp"];
        Assert.Equal(Generations.Unclassified, ambiguous.Generation);
        Assert.Contains(ScanFlags.AmbiguousName, ambiguous.Flags);

        // Filename says Switch 2 but the folder was assigned Switch 1: unclassified for review.
        var s2 = e["Puzzle Thing (Switch 2 Edition).nsp"];
        Assert.Equal("Puzzle Thing Switch 2 Edition", s2.Title);
        Assert.Equal(Generations.Unclassified, s2.Generation);
        Assert.Contains(ScanFlags.GenerationConflict, s2.Flags);
    }

    [Fact]
    public void Folder_generation_reassignment_respects_manual_edits()
    {
        var result = LibraryScanner.ScanFolder(_env.CreateSampleFolder(), Generations.Unclassified);
        var e = ByName(result);
        Assert.Equal(Generations.Switch2, e["Puzzle Thing (Switch 2 Edition).nsp"].Generation);
        var manual = e["Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp"];
        manual.Generation = Generations.Switch2;
        manual.GenerationSource = GenerationSources.Manual;

        LibraryScanner.ApplyFolderGeneration(result.Entries, Generations.Switch1);
        Assert.Equal(Generations.Switch1, e["Super Placeholder Quest [0100AAAA00000000][v0].nsp"].Generation);
        Assert.Equal(Generations.Switch2, manual.Generation);
        Assert.Equal(Generations.Unclassified, e["0100CCCC00000000.nsp"].Generation);
    }

    [Fact]
    public void Unknown_files_default_to_unclassified()
    {
        var e = LibraryScanner.ClassifyFile(@"C:\x\Some Game.nsp");
        Assert.Equal(ContentKinds.Base, e.Kind);
        Assert.Null(e.GenerationHint);
        Assert.False(e.AmbiguousName);
        LibraryScanner.ApplyFolderGeneration([e], Generations.Unclassified);
        Assert.Equal(Generations.Unclassified, e.Generation);
    }

    [Fact]
    public void Scan_leaves_files_untouched()
    {
        var folder = _env.CreateSampleFolder();
        Dictionary<string, (long, DateTime, string)> Snapshot() =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, p =>
            {
                var fi = new FileInfo(p);
                return (fi.Length, fi.LastWriteTimeUtc, Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(p))));
            });

        var before = Snapshot();
        var result = LibraryScanner.ScanFolder(folder, Generations.Switch1);
        Assert.Equal(before, Snapshot());
        Assert.Equal(8, result.Entries.Count);
    }

    [Fact]
    public void Repeat_scan_preserves_records()
    {
        var folder = _env.CreateSampleFolder();
        using var lib = _env.OpenLibrary();
        var first = LibraryScanner.ScanFolder(folder, Generations.Switch1, lib);
        var summary = LibraryScanner.ImportEntries(lib, folder, Generations.Switch1, first.Entries);
        Assert.Equal(5, summary.Added.Count); // 8 found - update - dlc - duplicate
        var idsBefore = lib.ListEditions().Select(e => e.Id).ToHashSet();

        // A user edit must survive a rescan, and nothing may be duplicated.
        var someId = idsBefore.First();
        lib.SetGeneration(someId, Generations.Switch2);

        var second = LibraryScanner.ScanFolder(folder, Generations.Switch1, lib);
        Assert.Equal(5, second.Entries.Count(e => e.InLibrary));
        Assert.All(second.Entries.Where(e => e.InLibrary), e => Assert.False(e.Include));
        var summary2 = LibraryScanner.ImportEntries(lib, folder, Generations.Switch1, second.Entries);
        Assert.Empty(summary2.Added);
        Assert.Equal(5, summary2.AlreadyPresent);

        var after = lib.ListEditions().ToDictionary(e => e.Id);
        Assert.Equal(idsBefore, after.Keys.ToHashSet());
        Assert.Equal(Generations.Switch2, after[someId].Generation);
    }

    [Fact]
    public void Missing_paths_reported_on_rescan()
    {
        var folder = _env.CreateSampleFolder();
        using var lib = _env.OpenLibrary();
        LibraryScanner.ImportEntries(lib, folder, Generations.Switch1, LibraryScanner.ScanFolder(folder, Generations.Switch1, lib).Entries);
        var moved = Path.Combine(folder, "Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp");
        File.Move(moved, Path.Combine(_env.Root, "moved.nsp"));

        var rescan = LibraryScanner.ScanFolder(folder, Generations.Switch1, lib);
        Assert.Contains(moved, rescan.MissingKnown);
        Assert.Equal(["Arcade Archives SAMPLE BLASTER"], lib.ListEditions().Where(e => e.IsMissing).Select(e => e.Title));
    }
}
