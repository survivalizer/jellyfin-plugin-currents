using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Scheduled task wrapper for <see cref="CatalogSyncService"/>.</summary>
public sealed class CatalogSyncTask : IScheduledTask
{
    private readonly CatalogSyncService _sync;

    public CatalogSyncTask(CatalogSyncService sync) => _sync = sync;

    public string Name => "Sync AIOMetadata catalogs";

    public string Key => "CurrentsCatalogSync";

    public string Description => "Writes the selected AIOMetadata catalogs into the Currents library folders.";

    public string Category => "Currents";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) =>
        _sync.SyncAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks };
    }
}
