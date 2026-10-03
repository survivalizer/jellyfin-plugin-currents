using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>"Verify library": rewrites Currents titles whose files are missing and forgets settings of deleted Jellyfin users. Manual.</summary>
public sealed class VerifyLibraryTask : IScheduledTask
{
    private readonly LibraryMaintenance _maintenance;

    public VerifyLibraryTask(LibraryMaintenance maintenance) => _maintenance = maintenance;

    public string Name => "Verify library";

    public string Key => TaskKeys.VerifyLibrary;

    public string Description => "Rewrites Currents titles whose files are missing (for example after a restore) and forgets the settings of deleted Jellyfin users.";

    public string Category => "Currents";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => _maintenance.VerifyAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
