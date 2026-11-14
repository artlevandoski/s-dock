using System.ComponentModel;
using System.Diagnostics;

namespace SwitchYard.Core;

public static class LaunchArgs
{
    public const string GamePlaceholder = "{game}";

    /// <summary>
    /// Eden (a Switch 1 emulator) accepts "-g &lt;path&gt;" to boot a game file directly.
    /// Verified on Eden v0.2.1 for Windows (2026-11-13). Re-check with Test Launch on
    /// other builds before labeling them supported.
    /// </summary>
    public static readonly IReadOnlyList<string> EdenPreset = ["-g", GamePlaceholder];

    public static List<string> Build(IEnumerable<string> args, string gamePath) =>
        args.Select(a => a.Replace(GamePlaceholder, gamePath, StringComparison.Ordinal)).ToList();

    public static string EffectiveWorkingDir(EmulatorProfile profile) =>
        string.IsNullOrWhiteSpace(profile.WorkingDir)
            ? Path.GetDirectoryName(profile.Executable) ?? ""
            : profile.WorkingDir.Trim();

    /// <summary>Human-readable problems that would prevent a launch.</summary>
    public static List<string> Validate(EmulatorProfile? profile, string? gamePath)
    {
        if (profile is null)
            return ["No emulator is set up yet. Open Emulator Settings and choose your Eden executable."];
        var problems = new List<string>();
        var exe = profile.Executable.Trim();
        if (exe.Length == 0)
            problems.Add("The emulator profile has no executable selected.");
        else if (!File.Exists(exe))
            problems.Add($"Emulator executable not found:\n{exe}");
        if (!string.IsNullOrWhiteSpace(profile.WorkingDir) && !Directory.Exists(profile.WorkingDir))
            problems.Add($"Working directory not found:\n{profile.WorkingDir}");
        if (string.IsNullOrEmpty(gamePath))
            problems.Add("This library entry has no game file.");
        else if (!File.Exists(gamePath))
            problems.Add($"Game file not found:\n{gamePath}");
        if (!profile.Args.Any(a => a.Contains(GamePlaceholder, StringComparison.Ordinal)))
            problems.Add($"The argument list doesn't contain {GamePlaceholder}, so the game path would not be passed.");
        return problems;
    }

    /// <summary>Display-only rendering of the command (Windows-style quoting).</summary>
    public static string Describe(EmulatorProfile profile, string gamePath)
    {
        var parts = new List<string> { string.IsNullOrEmpty(profile.Executable) ? "<executable>" : profile.Executable };
        parts.AddRange(Build(profile.Args, gamePath));
        return string.Join(" ", parts.Select(p => p.Length == 0 || p.Contains(' ') ? $"\"{p}\"" : p));
    }
}

/// <summary>
/// Starts the emulator with the program and each argument passed separately (no shell, no string
/// splitting), so paths containing spaces are safe. Play and Test Launch share one instance.
/// Events are raised on the SynchronizationContext captured at construction (the UI thread in the app).
/// </summary>
public sealed class EmulatorLauncher : IDisposable
{
    public event Action<string, int>? Launched;        // tag, pid
    public event Action<string, int, bool>? Finished;  // tag, exit code, crashed
    public event Action<string, string>? Failed;       // tag, message

    private readonly SynchronizationContext? _context;
    private readonly string? _logPath;
    private readonly object _logLock = new();
    private Process? _process;
    private StreamWriter? _log;

    public EmulatorLauncher(string? logPath = null)
    {
        _logPath = logPath;
        _context = SynchronizationContext.Current;
    }

    public bool IsRunning => _process is not null;
    public string CurrentTag { get; private set; } = "";

    private void Raise(Action action)
    {
        if (_context is not null) _context.Post(_ => action(), null);
        else action();
    }

    /// <summary>Start the emulator. Returns false (and raises <see cref="Failed"/>) if it couldn't start.</summary>
    public bool Launch(EmulatorProfile profile, string gamePath, string tag = "")
    {
        if (IsRunning)
        {
            Raise(() => Failed?.Invoke(tag, "An emulator is already running. Close it before launching another game."));
            return false;
        }
        var problems = LaunchArgs.Validate(profile, gamePath);
        if (problems.Count > 0)
        {
            var message = string.Join("\n\n", problems);
            Raise(() => Failed?.Invoke(tag, message));
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = profile.Executable,
            WorkingDirectory = LaunchArgs.EffectiveWorkingDir(profile),
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = _logPath is not null,
            RedirectStandardError = _logPath is not null,
        };
        foreach (var a in LaunchArgs.Build(profile.Args, gamePath))
            psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (_logPath is not null)
        {
            try
            {
                _log = new StreamWriter(_logPath, append: false) { AutoFlush = true };
                _log.WriteLine($"# {LibraryStore.UtcNow()}  {LaunchArgs.Describe(profile, gamePath)}");
            }
            catch (IOException) { _log = null; }
            proc.OutputDataReceived += (_, e) => WriteLog(e.Data);
            proc.ErrorDataReceived += (_, e) => WriteLog(e.Data);
        }
        proc.Exited += OnExited;

        _process = proc;
        CurrentTag = tag;
        try
        {
            proc.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Cleanup();
            var message = "The emulator could not be started.\n\n" + ex.Message +
                          "\n\nCheck that the executable is a valid Windows program and that you have permission to run it.";
            Raise(() => Failed?.Invoke(tag, message));
            return false;
        }
        if (_logPath is not null)
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        var pid = proc.Id;
        Raise(() => Launched?.Invoke(tag, pid));
        return true;
    }

    /// <summary>Force the emulator to stop (used only when the user chooses to quit anyway).</summary>
    public void Stop()
    {
        var p = _process;
        if (p is null) return;
        try
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
        }
        catch (InvalidOperationException) { }
    }

    private void WriteLog(string? line)
    {
        if (line is null) return;
        lock (_logLock) _log?.WriteLine(line);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        var p = (Process)sender!;
        try { p.WaitForExit(); } catch (InvalidOperationException) { } // flush async output
        var code = p.ExitCode;
        // NTSTATUS failure codes (0xC0000000 and up, e.g. access violation) come back negative.
        var crashed = code < 0;
        var tag = CurrentTag;
        Raise(() =>
        {
            Cleanup();
            Finished?.Invoke(tag, code, crashed);
        });
    }

    private void Cleanup()
    {
        lock (_logLock)
        {
            _log?.Dispose();
            _log = null;
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        if (_process is not null)
        {
            _process.Exited -= OnExited;
            Cleanup();
        }
    }
}
