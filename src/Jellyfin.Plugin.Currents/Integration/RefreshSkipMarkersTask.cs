using System.Diagnostics.CodeAnalysis;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>"Fetch skip markers": runs Jellyfin's segment providers for every Currents movie and episode. Manual; a sync that wrote titles queues it.</summary>
public sealed class RefreshSkipMarkersTask : IScheduledTask
{
    private readonly ILibraryManager _library;
    private readonly IMediaSegmentManager _segments;
    private readonly CurrentsItemLocator _locator;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<RefreshSkipMarkersTask> _logger;

    public RefreshSkipMarkersTask(ILibraryManager library, IMediaSegmentManager segments, CurrentsItemLocator locator, ICurrentsSettings settings, ILogger<RefreshSkipMarkersTask> logger)
    {
        _library = library;
        _segments = segments;
        _locator = locator;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "Fetch skip markers";

    public string Key => TaskKeys.SkipMarkers;

    public string Description => "Fetches skip-intro and credits markers for Currents titles from TheIntroDB, AniSkip and PublicMetaDB.";

    public string Category => "Currents";

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One item's providers may throw anything; the rest of the library must still get markers. Cancellation still propagates.")]
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!_settings.Current.EnableSegments)
        {
            progress.Report(100);
            return;
        }

        var items = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
        })
            .Where(i => _locator.TryGetTitle(i, out _))
            .ToList();

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _segments.RunSegmentPluginProviders(items[i], _library.GetLibraryOptions(items[i]), false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not fetch skip markers for {Name}: {Reason}", items[i].Name, SecretMasker.Mask(ex.Message));
            }

            progress.Report((i + 1) * 100.0 / items.Count);
        }

        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
