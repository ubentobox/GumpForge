using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Services;

/// <summary>
/// LRU cache for decoded gump art bitmaps. Keyed by (source, gumpId).
/// Default cap: 256 MB. Entries are evicted when the cache exceeds the cap.
/// </summary>
public partial class AssetCache : ObservableObject
{
    private readonly ConcurrentDictionary<(string Source, int GumpId), CacheEntry> _cache = new();
    private readonly LinkedList<(string Source, int GumpId)> _lruOrder = new();
    private readonly object _lruLock = new();
    private long _currentSizeBytes;

    [ObservableProperty] private long _maxSizeBytes = 256 * 1024 * 1024; // 256 MB
    [ObservableProperty] private int _entryCount;

    /// <summary>
    /// Try to get a cached bitmap. Returns null if not cached.
    /// </summary>
    public byte[]? TryGet(string source, int gumpId)
    {
        var key = (source, gumpId);
        if (_cache.TryGetValue(key, out var entry))
        {
            TouchLru(key);
            return entry.PixelData;
        }
        return null;
    }

    /// <summary>
    /// Store a decoded bitmap in the cache.
    /// </summary>
    public void Put(string source, int gumpId, byte[] pixelData, int width, int height)
    {
        var key = (source, gumpId);
        var entry = new CacheEntry(pixelData, width, height);
        long entrySize = pixelData.Length;

        // Evict until we have room
        while (_currentSizeBytes + entrySize > MaxSizeBytes && _lruOrder.Count > 0)
            EvictOldest();

        if (_cache.TryAdd(key, entry))
        {
            Interlocked.Add(ref _currentSizeBytes, entrySize);
            lock (_lruLock)
                _lruOrder.AddLast(key);
            EntryCount = _cache.Count;
        }
    }

    /// <summary>Clear the entire cache.</summary>
    public void Clear()
    {
        _cache.Clear();
        lock (_lruLock)
            _lruOrder.Clear();
        _currentSizeBytes = 0;
        EntryCount = 0;
    }

    private void TouchLru((string Source, int GumpId) key)
    {
        lock (_lruLock)
        {
            _lruOrder.Remove(key);
            _lruOrder.AddLast(key);
        }
    }

    private void EvictOldest()
    {
        (string Source, int GumpId) key;
        lock (_lruLock)
        {
            if (_lruOrder.First is null) return;
            key = _lruOrder.First.Value;
            _lruOrder.RemoveFirst();
        }

        if (_cache.TryRemove(key, out var entry))
        {
            Interlocked.Add(ref _currentSizeBytes, -entry.PixelData.Length);
            EntryCount = _cache.Count;
        }
    }

    private record CacheEntry(byte[] PixelData, int Width, int Height);
}
