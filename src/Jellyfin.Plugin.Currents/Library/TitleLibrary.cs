using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>
/// The one shared copy of the sync state. Catalog sync and search-add both change titles, so every read, every
/// change and every title folder write runs under one lock. Callers do their network calls before entering.
/// </summary>
public sealed class TitleLibrary
{
    private readonly object _gate = new();
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<TitleLibrary> _logger;
    private StateStore? _state;

    public TitleLibrary(ICurrentsSettings settings, TimeProvider time, ILogger<TitleLibrary> logger)
    {
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public T Use<T>(Func<StateStore, T> action)
    {
        lock (_gate)
        {
            _state ??= StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), _logger, SearchIdOf);
            return action(_state);
        }
    }

    public void Use(Action<StateStore> action) => Use(state =>
    {
        action(state);
        return true;
    });

    /// <summary>Computes a title's search id with the install secret.</summary>
    /// <param name="stateId">The state id.</param>
    /// <returns>The search id.</returns>
    public Guid SearchIdOf(string stateId) => SearchItemId.For(_settings.Current.SigningSecret, stateId);

    public TitleState? Get(string stateId) => Use(state => Copy(state.Get(stateId)));

    public TitleState? FindBySearchId(Guid searchId) => Use(state => Copy(state.FindBySearchId(searchId)));

    public LibraryWriter CreateWriter()
    {
        var config = _settings.Current;
        return new LibraryWriter(LibraryPaths.FromSettings(_settings), new StrmSigner(config.SigningSecret), config.StrmBaseUrl, _time, _logger);
    }

    /// <summary>Writes a title opened from search. A title already known keeps its folder and its catalog origin.</summary>
    public WriteResult AddFromSearch(TitleKey key, StremioMeta meta)
    {
        var writer = CreateWriter();
        return Use(state =>
        {
            var existing = state.Get(key.StateId);
            var result = key.Kind == MediaKind.Movie
                ? writer.WriteMovie(key, meta, existing?.Folder)
                : writer.WriteSeries(key, meta, existing?.Folder);

            var entry = existing ?? new TitleState { StateId = key.StateId, Kind = key.Kind, StremioId = key.StremioId, AddedBySearch = true };
            entry.Folder = result.RelativeFolder;
            entry.LastSeen = _time.GetUtcNow();
            state.Upsert(entry);
            state.Save();
            return result;
        });
    }

    private static TitleState? Copy(TitleState? title) => title is null
        ? null
        : new TitleState
        {
            StateId = title.StateId,
            Kind = title.Kind,
            StremioId = title.StremioId,
            Folder = title.Folder,
            Catalogs = [.. title.Catalogs],
            AddedBySearch = title.AddedBySearch,
            MissCount = title.MissCount,
            LastSeen = title.LastSeen,
        };
}
