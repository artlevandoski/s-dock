using Avalonia.Media.Imaging;

namespace SwitchYard.App.Controls;

/// <summary>
/// Loads artwork files as bitmaps scaled down to the size they're shown at, and keeps them so
/// scrolling the grid doesn't decode the same image again. A changed file (new mtime) reloads.
/// </summary>
public static class ArtCache
{
    private static readonly Dictionary<(string Path, int Width), (DateTime Stamp, Bitmap? Bitmap)> Cache = new();
    private static readonly object Lock = new();

    public static Bitmap? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch (IOException) { return null; }
        var key = (path, decodeWidth);
        lock (Lock)
            if (Cache.TryGetValue(key, out var hit) && hit.Stamp == stamp)
                return hit.Bitmap;

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(path);
            bitmap = Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            bitmap = null; // unreadable or not an image: fall back to the placeholder
        }
        lock (Lock)
            Cache[key] = (stamp, bitmap);
        return bitmap;
    }

    public static Bitmap? FromBytes(byte[] bytes, int decodeWidth)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, decodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
