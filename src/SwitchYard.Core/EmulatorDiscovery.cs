using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SwitchYard.Core;

public sealed record DiscoveredEmulator(string Path, string SuggestedName, string? Version, bool LooksLikeEden)
{
    public string Display => Version is null ? SuggestedName : $"{SuggestedName}  ({Version})";
}

/// <summary>Finds emulator executables in the Emulators folder. Reads file metadata only; runs nothing.</summary>
public static partial class EmulatorDiscovery
{
    // Uninstallers, crash reporters, updaters and command-line companions (e.g. eden-cli.exe) aren't what Play should start.
    [GeneratedRegex(@"^(unins.*|.*crash.*|.*update.*|.*installer.*|setup.*|vc_?redist.*|dotnet.*|.*helper.*|maintenancetool|.*uninstall.*|createdump|qtwebengine.*|.*[-_]cli|cli)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotAnEmulatorRe();

    public static List<DiscoveredEmulator> Find(string root, int maxDepth = 3)
    {
        var results = new List<DiscoveredEmulator>();
        if (!Directory.Exists(root)) return results;
        Walk(Path.GetFullPath(root), Path.GetFullPath(root), 0, maxDepth, results);
        // Several programs in one build folder would share a name; tell them apart by file name.
        var named = results
            .GroupBy(r => r.SuggestedName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Count() == 1 ? g : g.Select(r => r with { SuggestedName = $"{r.SuggestedName} ({Path.GetFileName(r.Path)})" }));
        return named.OrderBy(r => r.SuggestedName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(string root, string dir, int depth, int maxDepth, List<DiscoveredEmulator> results)
    {
        string[] files, dirs;
        try
        {
            files = Directory.GetFiles(dir, "*.exe");
            dirs = depth < maxDepth ? Directory.GetDirectories(dir) : [];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }
        foreach (var exe in files)
        {
            var stem = Path.GetFileNameWithoutExtension(exe);
            if (NotAnEmulatorRe().IsMatch(stem)) continue;
            results.Add(Describe(root, exe));
        }
        foreach (var d in dirs)
            Walk(root, d, depth + 1, maxDepth, results);
    }

    public static DiscoveredEmulator Describe(string root, string exe)
    {
        string? product = null, version = null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            product = string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName.Trim();
            var v = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            // Drop build metadata such as "+09f826d…" from informational versions.
            version = string.IsNullOrWhiteSpace(v) ? null : v.Split('+')[0].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        var stem = Path.GetFileNameWithoutExtension(exe);
        var parent = Path.GetDirectoryName(exe)!;
        // A build folder such as "Eden 0.2.1" is usually the best name; otherwise use product/file name.
        var name = !string.Equals(Path.GetFullPath(parent).TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(parent)
            : product ?? stem;
        var looksLikeEden = stem.Contains("eden", StringComparison.OrdinalIgnoreCase) ||
                            (product?.Contains("eden", StringComparison.OrdinalIgnoreCase) ?? false);
        return new DiscoveredEmulator(exe, name, version, looksLikeEden);
    }
}
