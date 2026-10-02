using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Persists <see cref="TitleState"/> entries in a JSON file in the plugin data folder.</summary>
public sealed class StateStore
{
    private readonly string _path;
    private readonly Dictionary<string, TitleState> _titles;
    private readonly Dictionary<Guid, string> _bySearchId = [];

    private StateStore(string path, Dictionary<string, TitleState> titles)
    {
        _path = path;
        _titles = titles;
        foreach (var id in titles.Keys)
        {
            _bySearchId[SearchItemId.For(id)] = id;
        }
    }

    /// <summary>Gets a snapshot of the titles; later changes to the store do not affect it.</summary>
    public IReadOnlyList<TitleState> Titles => _titles.Values.ToList();

    public static StateStore Load(string path, ILogger logger)
    {
        var titles = new Dictionary<string, TitleState>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return new StateStore(path, titles);
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

        return new StateStore(path, titles);
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

    public void Upsert(TitleState title)
    {
        _titles[title.StateId] = title;
        _bySearchId[SearchItemId.For(title.StateId)] = title.StateId;
    }

    public bool Remove(string stateId)
    {
        _bySearchId.Remove(SearchItemId.For(stateId));
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
