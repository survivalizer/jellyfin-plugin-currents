using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>"Clear stream cache": forgets every cached AIOStreams stream list and failure, so titles search again on their next open. Manual.</summary>
public sealed class ClearStreamCacheTask : IScheduledTask
{
    private readonly IStreamService _streams;

    public ClearStreamCacheTask(IStreamService streams) => _streams = streams;

    public string Name => "Clear stream cache";

    public string Key => TaskKeys.ClearStreamCache;

    public string Description => "Forgets the cached AIOStreams results so every title searches again when it is next opened.";

    public string Category => "Currents";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _streams.Clear();
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
