using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Scheduled task wrapper for <see cref="CatalogSyncService"/>.</summary>
public sealed class CatalogSyncTask : IScheduledTask
{
    private readonly CatalogSyncService _sync;
    private readonly ITaskManager _tasks;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<CatalogSyncTask> _logger;

    public CatalogSyncTask(CatalogSyncService sync, ITaskManager tasks, ICurrentsSettings settings, ILogger<CatalogSyncTask> logger)
    {
        _logger = logger;
        _sync = sync;
        _tasks = tasks;
        _settings = settings;
    }

    public string Name => "Sync AIOMetadata catalogs";

    public string Key => TaskKeys.CatalogSync;

    public string Description => "Writes the selected AIOMetadata catalogs into the Currents library folders.";

    public string Category => "Currents";

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Queueing skip markers is an extra: it must never fail a finished sync; cancellation still propagates.")]
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var report = await _sync.SyncAsync(progress, cancellationToken).ConfigureAwait(false);
        try
        {
            SkipMarkerQueue.AfterSync(report, _tasks, _settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sync itself succeeded; markers are picked up by Jellyfin's own schedule.
            _logger.LogWarning("Could not queue the skip-marker task after the sync: {Reason}", SecretMasker.Mask(ex.Message));
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks };
    }
}
