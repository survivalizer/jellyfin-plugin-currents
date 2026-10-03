namespace Jellyfin.Plugin.Currents.Library;

/// <summary>What search-add needs from Jellyfin's library. Implemented in Integration/ (JellyfinLibraryItems).</summary>
public interface ILibraryItems
{
    /// <summary>Gets the Jellyfin item for a Currents title folder, if Jellyfin has it.</summary>
    /// <param name="title">The Currents title.</param>
    /// <returns>The item id, or null.</returns>
    Guid? FindTitle(TitleState title);

    /// <summary>Gets items the user can see with one of the keys' IMDb/TMDB/TVDB/Currents ids and the key's kind, by state id. One query.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="keys">The title keys.</param>
    /// <returns>Item ids keyed by state id.</returns>
    IReadOnlyDictionary<string, Guid> FindExisting(Guid userId, IReadOnlyCollection<TitleKey> keys);

    /// <summary>Gets whether the user can see a library whose locations include the Currents folder for this kind and has no parental controls (remote results carry no rating to filter on).</summary>
    /// <param name="userId">The user.</param>
    /// <param name="kind">The media kind.</param>
    /// <returns>True when the user can see such a library.</returns>
    bool CanAdd(Guid userId, MediaKind kind);

    /// <summary>Creates (or finds) the Jellyfin item for a just-written title folder and refreshes it within a bounded time; null when no library holds the kind's folder.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="relativeFolder">The title folder, relative to the Currents library root.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The item id, or null.</returns>
    Task<Guid?> AddAsync(MediaKind kind, string relativeFolder, CancellationToken cancellationToken);

    /// <summary>Stops Jellyfin's realtime library monitor from reacting to Currents' own writes under the kind's folder until the returned handle is disposed.</summary>
    /// <param name="kind">The media kind.</param>
    /// <returns>A handle; dispose it to resume monitoring.</returns>
    IDisposable PauseMonitoring(MediaKind kind);

    /// <summary>Removes Jellyfin's items for these ids from the library database without touching files (used by Purge, because Jellyfin skips an empty library folder and would keep listing them).</summary>
    /// <param name="itemIds">The Jellyfin item ids.</param>
    void RemoveItems(IReadOnlyCollection<Guid> itemIds);

    /// <summary>Gets the kinds whose Currents folder is a location of the library (collection folder) with this id.</summary>
    /// <param name="libraryId">The library id.</param>
    /// <returns>The kinds held.</returns>
    IReadOnlyCollection<MediaKind> KindsIn(Guid libraryId);
}
