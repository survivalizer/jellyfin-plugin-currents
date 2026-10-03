using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Queues "Fetch skip markers" after a sync that wrote titles, so new titles get markers without waiting for Jellyfin's 12-hourly task.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Not a collection: the name says what it does, queueing the skip-markers task.")]
public static class SkipMarkerQueue
{
    /// <summary>Queues the task when the sync wrote titles and markers are on.</summary>
    /// <param name="report">The sync's report.</param>
    /// <param name="tasks">Jellyfin's task manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <returns>True when the task was queued.</returns>
    public static bool AfterSync(SyncReport report, ITaskManager tasks, ICurrentsSettings settings)
    {
        if (report.Written == 0 || !settings.Current.EnableSegments
            || tasks.ScheduledTasks.FirstOrDefault(w => w.ScheduledTask.Key == TaskKeys.SkipMarkers) is not { } worker)
        {
            return false;
        }

        tasks.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
        return true;
    }
}
