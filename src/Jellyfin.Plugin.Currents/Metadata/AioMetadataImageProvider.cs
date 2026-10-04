using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Posters, backdrops, logos and episode thumbnails from AIOMetadata.</summary>
public sealed class AioMetadataImageProvider : IRemoteImageProvider
{
    private readonly MetaCache _metas;
    private readonly IHttpClientFactory _httpClientFactory;

    public AioMetadataImageProvider(MetaCache metas, IHttpClientFactory httpClientFactory)
    {
        _metas = metas;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "Currents (AIOMetadata)";

    public bool Supports(BaseItem item) => item is Movie or Series or Episode;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        item is Episode
            ? new[] { ImageType.Primary }
            : new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is Episode episode)
        {
            var seriesId = episode.Series?.GetProviderId(CurrentsProviderIds.Currents);
            if (string.IsNullOrEmpty(seriesId))
            {
                return [];
            }

            var seriesMeta = await _metas.GetAsync("series", seriesId, cancellationToken).ConfigureAwait(false);
            var video = seriesMeta is null ? null : MetaMapper.FindEpisode(seriesMeta, episode.ParentIndexNumber, episode.IndexNumber);
            return MetaMapper.IsHttpUrl(video?.Thumbnail)
                ? new[] { new RemoteImageInfo { ProviderName = Name, Url = video!.Thumbnail, Type = ImageType.Primary } }
                : Array.Empty<RemoteImageInfo>();
        }

        var id = item.GetProviderId(CurrentsProviderIds.Currents);
        if (string.IsNullOrEmpty(id))
        {
            return [];
        }

        var meta = await _metas.GetAsync(item is Movie ? "movie" : "series", id, cancellationToken).ConfigureAwait(false);
        return meta is null ? [] : MetaMapper.Images(meta, Name).ToList();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(HttpClientNames.Artwork).GetAsync(new Uri(url), cancellationToken);
}
