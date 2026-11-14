using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SDock.App.Controls;

/// <summary>
/// Decodes artwork on background threads and keeps it in memory, so covers, backgrounds and logos
/// appear instantly while browsing. Requests are served "urgent first"; <see cref="Prioritize"/> moves
/// the games around the selection to the front. Least-recently-used images are dropped beyond the
/// memory budget. A file that changed on disk (new mtime/size) is decoded again.
/// </summary>
public sealed class ImageCache
{
    /// <summary>Covers for the grid and carousel. Width is set from the display scaling at startup.</summary>
    public static readonly ImageCache Covers = new("covers", 260, 768L << 20);

    /// <summary>Landscape backgrounds behind the carousel and in the details banner.</summary>
    public static readonly ImageCache Backdrops = new("backdrops", 1280, 192L << 20);

    /// <summary>Clear logos.</summary>
    public static readonly ImageCache Logos = new("logos", 600, 96L << 20);

    private sealed class Entry
    {
        public Bitmap? Bitmap;
        public (DateTime, long) Stamp;
        public long Bytes;
        public bool Loading;
        public List<Action<Bitmap?>> Waiters = [];
        public LinkedListNode<string>? LruNode;
    }

    private readonly string _name;
    private readonly long _budget;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();     // most recently used at the end
    private readonly LinkedList<string> _queue = new();   // front is decoded next
    private readonly Dictionary<string, LinkedListNode<string>> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _work = new(0);
    private long _bytes;

    public int DecodeWidth { get; set; }
    public int Count { get { lock (_lock) return _entries.Count(e => e.Value.Bitmap is not null); } }
    public long Bytes { get { lock (_lock) return _bytes; } }

    /// <summary>Number of decodes still waiting (for tests and diagnostics).</summary>
    public int Pending { get { lock (_lock) return _queue.Count + _entries.Count(e => e.Value.Loading); } }

    private ImageCache(string name, int decodeWidth, long budgetBytes)
    {
        _name = name;
        DecodeWidth = decodeWidth;
        _budget = budgetBytes;
        var workers = Math.Clamp(Environment.ProcessorCount - 1, 1, 4);
        for (var i = 0; i < workers; i++)
            new Thread(Worker) { IsBackground = true, Name = $"ImageCache {name} {i}", Priority = ThreadPriority.BelowNormal }.Start();
    }

    private static (DateTime, long) StampOf(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : default;
        }
        catch (IOException) { return default; }
    }

    /// <summary>The decoded image if it's ready and the file hasn't changed; otherwise null.</summary>
    public Bitmap? TryGet(string path)
    {
        var stamp = StampOf(path); // file system check outside the lock
        lock (_lock)
        {
            if (!_entries.TryGetValue(path, out var e) || e.Bitmap is null) return null;
            if (e.Stamp != stamp) return null; // changed on disk: caller requests a fresh decode
            Touch(path, e);
            return e.Bitmap;
        }
    }

    /// <summary>
    /// Ensures the image is decoded; <paramref name="onReady"/> runs on the UI thread with the result
    /// (immediately if already cached). Urgent requests go to the front of the queue.
    /// </summary>
    public void Request(string path, Action<Bitmap?>? onReady = null, bool urgent = true)
    {
        Bitmap? ready = null;
        var hit = false;
        var stamp = StampOf(path); // file system check outside the lock
        lock (_lock)
        {
            if (_entries.TryGetValue(path, out var e) && e.Bitmap is not null && e.Stamp == stamp)
            {
                Touch(path, e);
                ready = e.Bitmap;
                hit = true;
            }
            else
            {
                if (e is null) _entries[path] = e = new Entry();
                if (onReady is not null) e.Waiters.Add(onReady);
                Enqueue(path, urgent);
            }
        }
        if (hit) onReady?.Invoke(ready);
    }

    /// <summary>Queues images to decode in the background (behind urgent requests).</summary>
    public void Prefetch(IEnumerable<string> paths)
    {
        lock (_lock)
            foreach (var p in paths)
            {
                if (_entries.TryGetValue(p, out var e) && e.Bitmap is not null) continue;
                if (e is null) _entries[p] = new Entry();
                Enqueue(p, urgent: false);
            }
    }

    /// <summary>Moves these images (in this order) to the front of the queue, e.g. the games around the selection.</summary>
    public void Prioritize(IEnumerable<string> pathsInOrder)
    {
        lock (_lock)
        {
            var list = pathsInOrder.ToList();
            for (var i = list.Count - 1; i >= 0; i--)
                if (_queued.TryGetValue(list[i], out var node))
                {
                    _queue.Remove(node);
                    _queue.AddFirst(node);
                }
        }
    }

    private void Enqueue(string path, bool urgent)
    {
        if (_entries.TryGetValue(path, out var e) && e.Loading) return;
        if (_queued.TryGetValue(path, out var existing))
        {
            if (!urgent) return;
            _queue.Remove(existing);
            _queue.AddFirst(existing);
            return;
        }
        var node = urgent ? _queue.AddFirst(path) : _queue.AddLast(path);
        _queued[path] = node;
        _work.Release();
    }

    private void Touch(string path, Entry e)
    {
        if (e.LruNode is not null) _lru.Remove(e.LruNode);
        e.LruNode = _lru.AddLast(path);
    }

    private void Worker()
    {
        while (true)
        {
            _work.Wait();
            string path;
            int width;
            lock (_lock)
            {
                if (_queue.First is not { } node) continue;
                path = node.Value;
                _queue.RemoveFirst();
                _queued.Remove(path);
                if (!_entries.TryGetValue(path, out var e)) continue;
                e.Loading = true;
                width = DecodeWidth;
            }

            var stamp = StampOf(path);
            Bitmap? bitmap = null;
            try
            {
                if (stamp != default)
                {
                    // Read the file in one go with sharing that lets it be replaced or deleted meanwhile (e.g. a new
                    // cover from Change Match), and close it before the slower decode.
                    using var memory = new MemoryStream();
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        file.CopyTo(memory);
                    memory.Position = 0;
                    bitmap = Bitmap.DecodeToWidth(memory, width, BitmapInterpolationMode.HighQuality);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or InvalidOperationException or NotSupportedException)
            {
                bitmap = null; // unreadable or not an image: the placeholder stays
            }

            List<Action<Bitmap?>> waiters;
            lock (_lock)
            {
                if (!_entries.TryGetValue(path, out var e)) continue;
                e.Loading = false;
                if (e.Bitmap is not null) _bytes -= e.Bytes;
                e.Bitmap = bitmap;
                e.Stamp = stamp;
                e.Bytes = bitmap is null ? 0 : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                _bytes += e.Bytes;
                Touch(path, e);
                waiters = e.Waiters;
                e.Waiters = [];
                Evict();
            }
            if (waiters.Count > 0)
                Dispatcher.UIThread.Post(() => { foreach (var w in waiters) w(bitmap); });
        }
    }

    /// <summary>Drops least-recently-used images beyond the budget (they're decoded again if needed).</summary>
    private void Evict()
    {
        while (_bytes > _budget && _lru.First is { } oldest)
        {
            _lru.RemoveFirst();
            if (_entries.TryGetValue(oldest.Value, out var e) && !e.Loading && e.Waiters.Count == 0)
            {
                _bytes -= e.Bytes;
                // Not disposed: a control may still be drawing it; the GC frees it once unused.
                _entries.Remove(oldest.Value);
            }
        }
    }
}
