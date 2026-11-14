using System.Text.Json;
using SDock.Core;

namespace SDock.Tests;

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
        Root = Path.Combine(Path.GetTempPath(), "S-Dock Tests", Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>An Emulators folder with two "Eden" builds and some files that aren't emulators.</summary>
    public string CreateEmulatorsFolder()
    {
        var dir = Path.Combine(Root, "emulators");
        foreach (var rel in new[] { "Eden 0.2.1/eden.exe", "Eden nightly/eden.exe", "Eden 0.2.1/unins000.exe",
                                    "Eden nightly/crashpad_handler.exe", "Eden nightly/eden-cli.exe", "Other Build/othernx.exe" })
        {
            var p = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.Copy(FakeEmulatorExe, p, overwrite: true);
        }
        File.WriteAllText(Path.Combine(dir, "README.md"), "not an exe");
        return dir;
    }

    public static string FakeEmulatorExe
    {
        get
        {
            // tests/SDock.Tests/bin/<cfg>/net10.0 -> tests/FakeEmulator/bin/<cfg>/net10.0
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

/// <summary>Waiting for artwork that the background image cache decodes.</summary>
public static class Ui
{
    public static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for the image cache");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // Headless animations (e.g. the carousel's enlarge transition) only advance on render ticks.
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        }
    }
}
