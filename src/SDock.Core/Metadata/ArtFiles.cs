namespace SDock.Core.Metadata;

/// <summary>Writing artwork files safely on Windows, where a file another program is reading can't be replaced.</summary>
public static class ArtFiles
{
    private const int Attempts = 8;

    /// <summary>
    /// Writes bytes via a temporary file and moves it into place, retrying briefly if the target is in use
    /// (the image cache reading it, antivirus scanning it).
    /// </summary>
    public static async Task WriteAsync(string target, byte[] bytes, CancellationToken ct = default)
    {
        var temp = target + ".download";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            if (TryMove(temp, target, last: attempt == Attempts)) return;
            await Task.Delay(25 * (attempt + 1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous version for code already on the UI thread. Implemented without async/await so it can
    /// never wait on a continuation that needs the very thread it is blocking.
    /// </summary>
    public static void Write(string target, byte[] bytes)
    {
        var temp = target + ".download";
        File.WriteAllBytes(temp, bytes);
        for (var attempt = 0; ; attempt++)
        {
            if (TryMove(temp, target, last: attempt == Attempts)) return;
            Thread.Sleep(25 * (attempt + 1));
        }
    }

    private static bool TryMove(string temp, string target, bool last)
    {
        try
        {
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && !last)
        {
            return false;
        }
    }
}
