namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Keeps one Jellyfin collection per ticked catalog. Implemented in Integration/ (JellyfinCollectionSync).</summary>
public interface ICollectionSync
{
    /// <summary>Creates or updates the collections. Titles Jellyfin has not scanned yet are added on a later sync.</summary>
    /// <param name="plans">One plan per ticked catalog that synced.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task.</returns>
    Task SyncAsync(IReadOnlyList<CollectionPlan> plans, CancellationToken cancellationToken);
}
