using SDock.Core;

namespace SDock.App;

/// <summary>
/// A short running log of what S-Dock did (games started and closed, exit presses, controllers, closing), in
/// data\logs\events.log, so a problem that can't be seen from outside (such as S-Dock closing by itself) leaves a
/// trail. It keeps only the last part of the file.
/// </summary>
public static class EventLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Lock = new();

    public static string Path => System.IO.Path.Combine(AppPaths.LogDir, "events.log");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    // Keep the newest half.
                    var text = File.ReadAllText(Path);
                    File.WriteAllText(Path, text[(text.Length / 2)..]);
                }
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
