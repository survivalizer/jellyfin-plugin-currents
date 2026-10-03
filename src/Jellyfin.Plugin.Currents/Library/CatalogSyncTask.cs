using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Scheduled task wrapper for <see cref="CatalogSyncService"/>.</summary>
public sealed class CatalogSyncTask : IScheduledTask
{
    private readonly CatalogSyncService _sync;
    private readonly ITaskManager _tasks;
    private readonly ICurrentsSettings _settings;

    public CatalogSyncTask(CatalogSyncService sync, ITaskManager tasks, ICurrentsSettings settings)
    {
        _sync = sync;
        _tasks = tasks;
        _settings = settings;
    }

    public string Name => "Sync AIOMetadata catalogs";

    public string Key => TaskKeys.CatalogSync;

    public string Description => "Writes the selected AIOMetadata catalogs into the Currents library folders.";

    public string Category => "Currents";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var report = await _sync.SyncAsync(progress, cancellationToken).ConfigureAwait(false);
        SkipMarkerQueue.AfterSync(report, _tasks, _settings);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks };
    }
}
