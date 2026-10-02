using System.Net;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class AioMetadataClientTests
{
    private const string Uuid = "0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d";
    private static readonly AioMetadataEndpoint Endpoint = Parse($"https://meta.example.com/stremio/{Uuid}/manifest.json");

    private static AioMetadataEndpoint Parse(string url)
    {
        AioMetadataEndpoint.TryParse(url, out var endpoint, out _);
        return endpoint!;
    }

    private static (AioMetadataClient Client, StubHttpHandler Stub, FakeHttpClientFactory Factory) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var factory = new FakeHttpClientFactory(stub);
        return (new AioMetadataClient(factory, NullLogger<AioMetadataClient>.Instance), stub, factory);
    }

    [Fact]
    public async Task Reads_manifest_catalogs_and_flags_required_extras()
    {
        var (client, _, factory) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiometadata/manifest.json")));

        var manifest = await client.GetManifestAsync(Endpoint, CancellationToken.None);

        Assert.Equal(3, manifest.Catalogs.Count);
        Assert.False(manifest.Catalogs[0].RequiresExtra);
        Assert.True(manifest.Catalogs[2].RequiresExtra);
        Assert.Equal(HttpClientNames.AioMetadata, Assert.Single(factory.RequestedNames));
    }

    [Fact]
    public async Task Reads_catalog_page_drops_entries_without_id_and_accepts_numeric_year()
    {
        var (client, stub, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiometadata/catalog-movie.json")));

        var metas = await client.GetCatalogPageAsync(Endpoint, "movie", "tmdb.top", 20, CancellationToken.None);

        Assert.EndsWith("/catalog/movie/tmdb.top/skip=20.json", stub.Requests[0].AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(2, metas.Count);
        Assert.Equal("1994", metas[0].Year);
        Assert.Equal("tt0111161", metas[0].ImdbId);
        Assert.Equal("8.8", metas[1].ImdbRating);
    }

    [Fact]
    public async Task Reads_series_meta_with_string_or_numeric_episode_numbers()
    {
        var (client, _, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiometadata/meta-series.json")));

        var meta = await client.GetMetaAsync(Endpoint, "series", "tt0944947", CancellationToken.None);

        Assert.NotNull(meta);
        Assert.Equal(3, meta!.Videos!.Count);
        Assert.Equal(2, meta.Videos[1].Episode);
        Assert.Equal("The Kingsroad", meta.Videos[1].Name);
    }

    [Fact]
    public async Task Meta_not_found_returns_null()
    {
        var (client, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetMetaAsync(Endpoint, "movie", "tt0000001", CancellationToken.None));
    }

    [Fact]
    public async Task Server_error_throws_with_masked_url()
    {
        var (client, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<AioMetadataException>(() => client.GetCatalogPageAsync(Endpoint, "movie", "tmdb.top", 0, CancellationToken.None));

        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Uuid, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_json_throws_aiometadata_exception()
    {
        var (client, _, _) = Create(_ => StubHttpHandler.Json("<html>oops</html>"));

        await Assert.ThrowsAsync<AioMetadataException>(() => client.GetManifestAsync(Endpoint, CancellationToken.None));
    }
}
