using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>
/// Marker lookups per title, in memory and on disk ({data}/segments/{key}.json). A lookup with markers is fresh for 7 days,
/// one without for 1 day. The gate still reads the reference runtime of a stale lookup: Jellyfin keeps the markers until the next fetch.
/// </summary>
public sealed class SegmentStore
{
    internal static readonly TimeSpan FoundTtl = TimeSpan.FromDays(7);
    internal static readonly TimeSpan MissTtl = TimeSpan.FromDays(1);
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, StoredLookup> _memory = new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;

    public SegmentStore(ICurrentsSettings settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
    }

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Gets the number of titles with a stored lookup.</summary>
    public int Count => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json").Length : 0;

    private string Folder => Path.Combine(_settings.DataFolderPath, "segments");

    public bool TryGetFresh(CurrentsTitle title, [NotNullWhen(true)] out SegmentLookup? lookup)
    {
        var stored = Load(title);
        if (stored is not null && stored.SavedAt + (stored.Lookup.Markers.Count > 0 ? FoundTtl : MissTtl) > _time.GetUtcNow())
        {
            Interlocked.Increment(ref _hits);
            lookup = stored.Lookup;
            return true;
        }

        Interlocked.Increment(ref _misses);
        lookup = null;
        return false;
    }

    public long? ReferenceTicks(CurrentsTitle title) => Load(title)?.Lookup.ReferenceTicks;

    public void Set(CurrentsTitle title, SegmentLookup lookup)
    {
        var key = Key(title);
        var stored = new StoredLookup(_time.GetUtcNow(), lookup);
        _memory[key] = stored;
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, key + ".json");
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, JsonDefaults.Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the in-memory entry still serves this process.
        }
    }

    public void Clear()
    {
        _memory.Clear();
        try
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open elsewhere stays until the next clear; the memory cache is already empty.
        }
    }

    internal static string Key(CurrentsTitle title) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title.Type + "/" + title.StremioId)))[..32];

    private StoredLookup? Load(CurrentsTitle title)
    {
        var key = Key(title);
        if (_memory.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(Folder, key + ".json");
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredLookup>(File.ReadAllText(path), JsonDefaults.Options);
            if (stored?.Lookup?.Markers is null)
            {
                return null;
            }

            _memory[key] = stored;
            return stored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record StoredLookup(DateTimeOffset SavedAt, SegmentLookup Lookup);
}
