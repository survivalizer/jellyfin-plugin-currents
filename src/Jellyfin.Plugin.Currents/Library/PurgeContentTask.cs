using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>"Purge Currents content": removes every Currents title and clears the Currents caches. Manual; the next sync writes the enabled catalogs again.</summary>
public sealed class PurgeContentTask : IScheduledTask
{
    private readonly LibraryMaintenance _maintenance;

    public PurgeContentTask(LibraryMaintenance maintenance) => _maintenance = maintenance;

    public string Name => "Purge Currents content";

    public string Key => TaskKeys.PurgeContent;

    public string Description => "Removes every Currents title from the library folders and clears the Currents caches. The next catalog sync writes the enabled catalogs again; untick catalogs first to keep them out.";

    public string Category => "Currents";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => _maintenance.PurgeAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
