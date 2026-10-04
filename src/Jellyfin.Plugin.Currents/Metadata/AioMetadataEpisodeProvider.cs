using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Episode metadata from the parent series' AIOMetadata meta.</summary>
public sealed class AioMetadataEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
{
    private readonly MetaCache _metas;
    private readonly IHttpClientFactory _httpClientFactory;

    public AioMetadataEpisodeProvider(MetaCache metas, IHttpClientFactory httpClientFactory)
    {
        _metas = metas;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "Currents (AIOMetadata)";

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();
        if (!info.SeriesProviderIds.TryGetValue(CurrentsProviderIds.Currents, out var seriesId) || string.IsNullOrEmpty(seriesId))
        {
            return result;
        }

        var meta = await _metas.GetAsync("series", seriesId, cancellationToken).ConfigureAwait(false);
        var video = meta is null ? null : MetaMapper.FindEpisode(meta, info.ParentIndexNumber, info.IndexNumber);
        if (video is null)
        {
            return result;
        }

        var episode = new Episode();
        MetaMapper.ApplyEpisode(video, episode);
        result.Item = episode;
        result.HasMetadata = true;
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken) =>
        Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(HttpClientNames.Artwork).GetAsync(new Uri(url), cancellationToken);
}
