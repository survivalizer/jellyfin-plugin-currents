using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Currents.Clients.Posters;

/// <summary>
/// Search-card posters in memory, by search id. Poster requests are anonymous, so this bounds what they can make the
/// server fetch: one upstream fetch per id at a time, a poster kept for an hour and a missing one for five minutes,
/// and at most 200 posters and 64 MB (the oldest go first).
/// </summary>
public sealed class PosterCache
{
    private static readonly TimeSpan ImageTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);
    private readonly IPosterClient _posters;
    private readonly TimeProvider _time;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _oldestFirst = new();
    private readonly ConcurrentDictionary<Guid, Lazy<Task<PosterImage?>>> _inFlight = new();
    private long _bytes;

    public PosterCache(IPosterClient posters, TimeProvider time)
        : this(posters, time, 200, 64L * 1024 * 1024)
    {
    }

    internal PosterCache(IPosterClient posters, TimeProvider time, int maxEntries, long maxBytes)
    {
        _posters = posters;
        _time = time;
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    /// <summary>Gets the number of cached answers (for tests).</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Gets the cached poster bytes (for tests).</summary>
    internal long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    public async Task<PosterImage?> GetAsync(Guid searchId, Uri uri, CancellationToken cancellationToken)
    {
        if (TryGet(searchId, out var cached))
        {
            return cached;
        }

        var lazy = _inFlight.GetOrAdd(searchId, id => new Lazy<Task<PosterImage?>>(() => FetchAsync(id, uri)));
        var fetch = lazy.Value;
        _ = fetch.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<Guid, Lazy<Task<PosterImage?>>>(searchId, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Shared by concurrent callers, so it never observes one caller's cancellation; PosterClient's own timeout bounds it.
    private async Task<PosterImage?> FetchAsync(Guid searchId, Uri uri)
    {
        var image = await _posters.GetAsync(uri, CancellationToken.None).ConfigureAwait(false);
        Store(searchId, image);
        return image;
    }

    private bool TryGet(Guid searchId, out PosterImage? image)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(searchId, out var node))
            {
                if (node.Value.Expires > _time.GetUtcNow())
                {
                    image = node.Value.Image;
                    return true;
                }

                RemoveNode(node);
            }
        }

        image = null;
        return false;
    }

    private void Store(Guid searchId, PosterImage? image)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(searchId, out var existing))
            {
                RemoveNode(existing);
            }

            var entry = new Entry(searchId, image, _time.GetUtcNow() + (image is null ? FailureTtl : ImageTtl));
            _entries[searchId] = _oldestFirst.AddLast(entry);
            _bytes += entry.Size;
            while (_oldestFirst.First is { } oldest && (_entries.Count > _maxEntries || _bytes > _maxBytes))
            {
                RemoveNode(oldest);
            }
        }
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        _oldestFirst.Remove(node);
        _entries.Remove(node.Value.SearchId);
        _bytes -= node.Value.Size;
    }

    private sealed record Entry(Guid SearchId, PosterImage? Image, DateTimeOffset Expires)
    {
        public long Size => Image?.Bytes.LongLength ?? 0;
    }
}
