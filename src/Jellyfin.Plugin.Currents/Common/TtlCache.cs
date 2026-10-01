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
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();
    private readonly TimeProvider _time;

    public TtlCache(TimeProvider time) => _time = time;

    public int Count => _entries.Count;

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
            foreach (var (k, e) in _entries)
            {
                if (e.Expires <= now)
                {
                    _entries.TryRemove(k, out _);
                }
            }
        }

        _entries[key] = new Entry(value, now + ttl);
    }

    public void Remove(TKey key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    private readonly record struct Entry(TValue Value, DateTimeOffset Expires);
}
