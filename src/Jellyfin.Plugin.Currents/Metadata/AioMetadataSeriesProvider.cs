using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Series metadata from AIOMetadata, looked up by the Currents provider id.</summary>
public sealed class AioMetadataSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
{
    private readonly MetaCache _metas;
    private readonly IHttpClientFactory _httpClientFactory;

    public AioMetadataSeriesProvider(MetaCache metas, IHttpClientFactory httpClientFactory)
    {
        _metas = metas;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "Currents (AIOMetadata)";

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
        if (!info.ProviderIds.TryGetValue(CurrentsProviderIds.Currents, out var id) || string.IsNullOrEmpty(id))
        {
            return result;
        }

        var meta = await _metas.GetAsync("series", id, cancellationToken).ConfigureAwait(false);
        if (meta is null)
        {
            return result;
        }

        var item = new Series();
        MetaMapper.Apply(meta, item);
        result.Item = item;
        result.HasMetadata = true;
        result.QueriedById = true;
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken) =>
        Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(HttpClientNames.Artwork).GetAsync(new Uri(url), cancellationToken);
}
