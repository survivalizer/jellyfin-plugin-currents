using Jellyfin.Plugin.Currents.Integration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Sets the runtime of Currents movies and episodes from AIOMetadata. Remote providers cannot: Jellyfin's merge skips RunTimeTicks for videos.</summary>
public sealed class AioRuntimeProvider : ICustomMetadataProvider<Movie>, ICustomMetadataProvider<Episode>
{
    private readonly MetaCache _metas;
    private readonly CurrentsItemLocator _locator;

    public AioRuntimeProvider(MetaCache metas, CurrentsItemLocator locator)
    {
        _metas = metas;
        _locator = locator;
    }

    public string Name => "Currents runtime";

    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken) =>
        ApplyAsync(item, cancellationToken);

    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken) =>
        ApplyAsync(item, cancellationToken);

    private async Task<ItemUpdateType> ApplyAsync(Video item, CancellationToken cancellationToken)
    {
        if (item.RunTimeTicks is > 0
            || item.LockedFields.Contains(MetadataField.Runtime)
            || !_locator.TryGetTitle(item, out var title))
        {
            return ItemUpdateType.None;
        }

        var meta = await _metas.GetAsync(title.Type, title.SeriesId, cancellationToken).ConfigureAwait(false);
        if (MetaMapper.ParseRuntimeTicks(meta?.Runtime) is not { } ticks || ticks <= 0)
        {
            return ItemUpdateType.None;
        }

        item.RunTimeTicks = ticks;
        return ItemUpdateType.MetadataImport;
    }
}
