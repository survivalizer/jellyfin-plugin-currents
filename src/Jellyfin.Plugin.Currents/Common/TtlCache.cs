using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>A small thread-safe in-memory cache with per-entry expiry.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The cached value type.</typeparam>
public sealed class TtlCache<TKey, TValue>
    where TKey : notnull
{
    private const int TrimThreshold = 5000;
    private static readonly long TrimIntervalTicks = TimeSpan.FromMinutes(1).Ticks;
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();
    private readonly TimeProvider _time;
    private long _nextTrimTicks;
    private int _trimCount;

    public TtlCache(TimeProvider time) => _time = time;

    public int Count => _entries.Count;

    /// <summary>Gets how many expiry scans have run (for tests).</summary>
    internal int TrimCount => Volatile.Read(ref _trimCount);

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.Expires > _time.GetUtcNow())
            {
                value = entry.Value;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value, TimeSpan ttl)
    {
        var now = _time.GetUtcNow();
        if (_entries.Count >= TrimThreshold)
        {
            TrimExpired(now);
        }

        _entries[key] = new Entry(value, now + ttl);
    }

    public void Remove(TKey key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    // Scans at most once a minute: with many live entries every Set would otherwise walk the whole map.
    private void TrimExpired(DateTimeOffset now)
    {
        var next = Volatile.Read(ref _nextTrimTicks);
        if (now.UtcTicks < next
            || Interlocked.CompareExchange(ref _nextTrimTicks, now.UtcTicks + TrimIntervalTicks, next) != next)
        {
            return;
        }

        Interlocked.Increment(ref _trimCount);
        foreach (var (k, e) in _entries)
        {
            if (e.Expires <= now)
            {
                _entries.TryRemove(k, out _);
            }
        }
    }

    private readonly record struct Entry(TValue Value, DateTimeOffset Expires);
}
