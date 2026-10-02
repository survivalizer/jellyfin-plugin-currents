using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>
/// Persists <see cref="TitleState"/> entries in a JSON file in the plugin data folder. Not thread-safe (callers go
/// through <see cref="TitleLibrary"/>'s lock), except <see cref="IsKnownSearchId"/>, which may be called at any time.
/// </summary>
public sealed class StateStore
{
    private readonly string _path;
    private readonly Dictionary<string, TitleState> _titles;
    private readonly ConcurrentDictionary<Guid, string> _bySearchId = new();
    private readonly Func<string, Guid>? _searchIdOf;

    private StateStore(string path, Dictionary<string, TitleState> titles, Func<string, Guid>? searchIdOf)
    {
        _path = path;
        _titles = titles;
        _searchIdOf = searchIdOf;
        if (searchIdOf is not null)
        {
            foreach (var id in titles.Keys)
            {
                _bySearchId[searchIdOf(id)] = id;
            }
        }
    }

    /// <summary>Gets a snapshot of the titles; later changes to the store do not affect it.</summary>
    public IReadOnlyList<TitleState> Titles => _titles.Values.ToList();

    /// <summary>Loads the state file.</summary>
    /// <param name="path">The state file.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="searchIdOf">Computes a title's search id from its state id; without it, nothing is found by search id.</param>
    /// <returns>The loaded store (empty when the file is missing or unreadable).</returns>
    public static StateStore Load(string path, ILogger logger, Func<string, Guid>? searchIdOf = null)
    {
        var titles = new Dictionary<string, TitleState>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return new StateStore(path, titles, searchIdOf);
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<TitleState?>>(File.ReadAllText(path), JsonDefaults.Options) ?? [];
            foreach (var title in list)
            {
                if (title is null || string.IsNullOrEmpty(title.StateId))
                {
                    continue;
                }

                Normalize(title);
                if (titles.TryGetValue(title.StateId, out var existing))
                {
                    logger.LogWarning("Currents sync state has duplicate entries for {StateId}; merging", title.StateId);
                    title.AddedBySearch |= existing.AddedBySearch;
                    title.Catalogs = existing.Catalogs.Concat(title.Catalogs).Distinct(StringComparer.Ordinal).ToList();
                    title.MissCount = Math.Min(existing.MissCount, title.MissCount);
                    title.LastSeen = title.LastSeen > existing.LastSeen ? title.LastSeen : existing.LastSeen;
                }

                titles[title.StateId] = title;
            }
        }
        catch (JsonException ex)
        {
            var aside = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, aside, overwrite: true);
            logger.LogError(ex, "Currents sync state was unreadable and was moved to {Path}; starting fresh", aside);
        }

        return new StateStore(path, titles, searchIdOf);
    }

    private static void Normalize(TitleState title)
    {
        // The file is untrusted JSON: null values can land in non-nullable properties.
#pragma warning disable CS8600, CS8601, IDE0074
        title.Folder ??= string.Empty;
        title.StremioId ??= string.Empty;
        title.Catalogs = (title.Catalogs ?? []).Where(c => !string.IsNullOrEmpty(c)).ToList();
#pragma warning restore CS8600, CS8601, IDE0074
    }

    public TitleState? Get(string stateId) => _titles.GetValueOrDefault(stateId);

    public TitleState? FindBySearchId(Guid searchId) =>
        _bySearchId.TryGetValue(searchId, out var stateId) ? _titles.GetValueOrDefault(stateId) : null;

    /// <summary>Gets whether a search id belongs to a title in the store. Lock-free; safe from any thread.</summary>
    /// <param name="searchId">The search id.</param>
    /// <returns>True when a title has this search id.</returns>
    public bool IsKnownSearchId(Guid searchId) => _bySearchId.ContainsKey(searchId);

    public void Upsert(TitleState title)
    {
        _titles[title.StateId] = title;
        if (_searchIdOf is not null)
        {
            _bySearchId[_searchIdOf(title.StateId)] = title.StateId;
        }
    }

    public bool Remove(string stateId)
    {
        if (_searchIdOf is not null)
        {
            _bySearchId.TryRemove(_searchIdOf(stateId), out _);
        }

        return _titles.Remove(stateId);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_titles.Values.OrderBy(t => t.StateId, StringComparer.Ordinal), JsonDefaults.Indented));
        File.Move(temp, _path, overwrite: true);
    }
}
