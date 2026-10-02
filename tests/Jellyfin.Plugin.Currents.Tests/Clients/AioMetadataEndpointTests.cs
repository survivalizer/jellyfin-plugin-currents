using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class AioMetadataEndpointTests
{
    private const string Url = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";

    [Fact]
    public void Parses_manifest_url_into_base()
    {
        Assert.True(AioMetadataEndpoint.TryParse(Url, out var endpoint, out var error));
        Assert.Null(error);
        Assert.Equal("https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/", endpoint!.BaseUri.ToString());
        Assert.Equal(Url, endpoint.Manifest.ToString());
    }

    [Fact]
    public void Builds_catalog_urls_with_and_without_skip()
    {
        AioMetadataEndpoint.TryParse(Url, out var endpoint, out _);

        Assert.EndsWith("/catalog/movie/tmdb.top.json", endpoint!.Catalog("movie", "tmdb.top", 0).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("/catalog/movie/tmdb.top/skip=40.json", endpoint.Catalog("movie", "tmdb.top", 40).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_meta_url_and_escapes_ids()
    {
        AioMetadataEndpoint.TryParse(Url, out var endpoint, out _);

        Assert.EndsWith("/meta/series/tt0944947.json", endpoint!.Meta("series", "tt0944947").ToString(), StringComparison.Ordinal);
        Assert.EndsWith("/meta/series/kitsu%3A1376.json", endpoint.Meta("series", "kitsu:1376").AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://meta.example.com/manifest.json")]
    [InlineData("https://meta.example.com/stremio/abc/configure")]
    public void Rejects_invalid_urls_with_a_message(string? url)
    {
        Assert.False(AioMetadataEndpoint.TryParse(url, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
