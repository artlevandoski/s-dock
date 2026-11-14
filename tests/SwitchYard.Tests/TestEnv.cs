using System.Text.Json;
using SwitchYard.Core;

namespace SwitchYard.Tests;

/// <summary>Per-test temp folders with placeholder game files and a fake emulator profile.</summary>
public sealed class TestEnv : IDisposable
{
    // Placeholder files only; contents are irrelevant to the scanner.
    public static readonly string[] SampleFiles =
    [
        "Super Placeholder Quest [0100AAAA00000000][v0].nsp",
        "Super Placeholder Quest [0100AAAA00000800][v131072].nsp",
        "Super Placeholder Quest Bonus Pack [0100AAAA00001001][v0].nsp",
        "Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp",
        "sub folder with spaces/Another Test Game.xci",
        "Another Test Game (copy).xci",
        "Puzzle Thing (Switch 2 Edition).nsp",
        "0100CCCC00000000.nsp",
        "notes.txt",
    ];

    public string Root { get; }
    public string DataDir => Path.Combine(Root, "app data");
    public string DbPath => Path.Combine(DataDir, "library.sqlite3");
    public string GameFolder => Path.Combine(Root, "My Games", "Switch Library");
    public string RecordPath => Path.Combine(Root, "launch record.json");
    public string EmulatorDir => Path.Combine(Root, "Emulator Folder With Spaces");

    public TestEnv()
    {
        Root = Path.Combine(Path.GetTempPath(), "SwitchYard Tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(EmulatorDir);
    }

    public string CreateSampleFolder()
    {
        foreach (var rel in SampleFiles)
        {
            var p = Path.Combine(GameFolder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "placeholder game file for tests");
        }
        return GameFolder;
    }

    public string Game(string name = "My Game (Rev 1) [0100AAAA00000000].nsp")
    {
        var p = Path.Combine(Root, "Games With Spaces", name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "placeholder");
        return p;
    }

    public LibraryStore OpenLibrary() => new(DbPath);

    public static string FakeEmulatorExe
    {
        get
        {
            // tests/SwitchYard.Tests/bin/<cfg>/net10.0 -> tests/FakeEmulator/bin/<cfg>/net10.0
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var tfm = Path.GetFileName(baseDir);
            var cfg = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
            var testsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var exe = Path.Combine(testsDir, "FakeEmulator", "bin", cfg, tfm, OperatingSystem.IsWindows() ? "FakeEmulator.exe" : "FakeEmulator");
            if (!File.Exists(exe))
                throw new FileNotFoundException("FakeEmulator was not built", exe);
            return exe;
        }
    }

    public EmulatorProfile FakeProfile(int exitCode = 0, double sleep = 0) => EmulatorProfile.New(
        name: "Fake Eden",
        executable: FakeEmulatorExe,
        workingDir: EmulatorDir,
        generation: Generations.Switch1,
        args: ["--record", RecordPath, "--exit-code", exitCode.ToString(),
               "--sleep", sleep.ToString(System.Globalization.CultureInfo.InvariantCulture), "-g", LaunchArgs.GamePlaceholder]);

    public JsonElement ReadRecord() => JsonDocument.Parse(File.ReadAllText(RecordPath)).RootElement;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
