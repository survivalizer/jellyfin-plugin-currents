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

    private StateStore(string path, Dictionary<string, TitleState> titles)
    {
        _path = path;
        _titles = titles;
    }

    public IReadOnlyCollection<TitleState> Titles => _titles.Values;

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

    public void Upsert(TitleState title) => _titles[title.StateId] = title;

    public bool Remove(string stateId) => _titles.Remove(stateId);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_titles.Values.OrderBy(t => t.StateId, StringComparer.Ordinal), JsonDefaults.Indented));
        File.Move(temp, _path, overwrite: true);
    }
}
