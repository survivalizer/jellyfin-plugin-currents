using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>ffprobe results per stream key, in memory and on disk ({data}/probes/{key}.json), so each file is probed once per install, not once per restart.</summary>
public sealed partial class ProbeCache
{
    internal const int DefaultMaxFiles = 5000;
    private const int PruneEvery = 200;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(10);
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly int _maxFiles;
    private readonly TtlCache<string, Entry> _memory;
    private int _writes;

    public ProbeCache(ICurrentsSettings settings, TimeProvider time)
        : this(settings, time, DefaultMaxFiles)
    {
    }

    internal ProbeCache(ICurrentsSettings settings, TimeProvider time, int maxFiles)
    {
        _settings = settings;
        _time = time;
        _maxFiles = maxFiles;
        _memory = new TtlCache<string, Entry>(time);
    }

    private string Folder => Path.Combine(_settings.DataFolderPath, "probes");

    /// <summary>Forgets every probe result, in memory and on disk.</summary>
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

    public bool TryGet(string streamKey, [NotNullWhen(true)] out ProbedMedia? media)
    {
        if (!_memory.TryGet(streamKey, out var entry))
        {
            var stored = Load(streamKey);
            entry = new Entry(stored?.Media);
            _memory.Set(streamKey, entry, stored is null ? MissTtl : stored.SavedAt + Ttl - _time.GetUtcNow());
        }

        media = entry.Media;
        return media is not null;
    }

    public void Set(string streamKey, ProbedMedia media)
    {
        _memory.Set(streamKey, new Entry(media), Ttl);
        if (!IsStreamKey(streamKey))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            var path = FileFor(streamKey);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoredProbe(_time.GetUtcNow(), media), JsonDefaults.Options));
            File.Move(temp, path, overwrite: true);
            PruneIfNeeded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the in-memory entry still serves this process.
        }
    }

    private static bool IsStreamKey(string key) => StreamKey().IsMatch(key);

    private string FileFor(string key) => Path.Combine(Folder, key + ".json");

    private StoredProbe? Load(string key)
    {
        if (!IsStreamKey(key))
        {
            return null;
        }

        var path = FileFor(key);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredProbe>(File.ReadAllText(path), JsonDefaults.Options);
            if (stored?.Media is null || stored.SavedAt + Ttl <= _time.GetUtcNow())
            {
                File.Delete(path);
                return null;
            }

            return stored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Checks on the first write and every 200th after it; deletes the least recently written files beyond the cap.
    private void PruneIfNeeded()
    {
        if (Interlocked.Increment(ref _writes) % PruneEvery != 1)
        {
            return;
        }

        var files = new DirectoryInfo(Folder).GetFiles("*.json");
        if (files.Length <= _maxFiles)
        {
            return;
        }

        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - _maxFiles))
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another writer or a locked file; the next prune tries again.
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex StreamKey();

    private sealed record Entry(ProbedMedia? Media);

    private sealed record StoredProbe(DateTimeOffset SavedAt, ProbedMedia Media);
}
