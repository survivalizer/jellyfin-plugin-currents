using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Metadata;

public class MetadataProviderTests
{
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeSettings _settings = new();

    public MetadataProviderTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _client.Metas["movie/tt1"] = new StremioMeta { Id = "tt1", Name = "Movie One", Year = "2001" };
        _client.Metas["series/tt2"] = new StremioMeta
        {
            Id = "tt2",
            Name = "Show Two",
            Videos = [new StremioVideo { Season = 1, Episode = 1, Title = "Pilot" }],
        };
    }

    private MetaCache Cache() => new(_client, _settings, new ManualTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<MetaCache>.Instance);

    [Fact]
    public async Task Movie_provider_maps_meta_found_by_currents_id()
    {
        var provider = new AioMetadataMovieProvider(Cache(), new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage())));
        var info = new MovieInfo();
        info.ProviderIds["Currents"] = "tt1";

        var result = await provider.GetMetadata(info, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("Movie One", result.Item.Name);
    }

    [Fact]
    public async Task Movie_provider_without_currents_id_returns_nothing()
    {
        var provider = new AioMetadataMovieProvider(Cache(), new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage())));

        var result = await provider.GetMetadata(new MovieInfo(), CancellationToken.None);

        Assert.False(result.HasMetadata);
        Assert.Empty(_client.MetaRequests);
    }

    [Fact]
    public async Task Episode_provider_reads_episode_from_series_meta()
    {
        var provider = new AioMetadataEpisodeProvider(Cache(), new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage())));
        var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1 };
        info.SeriesProviderIds["Currents"] = "tt2";

        var result = await provider.GetMetadata(info, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("Pilot", result.Item.Name);
    }

    [Fact]
    public async Task Meta_cache_fetches_once()
    {
        var cache = Cache();

        await cache.GetAsync("movie", "tt1", CancellationToken.None);
        await cache.GetAsync("movie", "tt1", CancellationToken.None);

        Assert.Single(_client.MetaRequests);
    }
}
