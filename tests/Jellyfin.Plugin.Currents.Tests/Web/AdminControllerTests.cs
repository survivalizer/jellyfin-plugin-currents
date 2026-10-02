using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public class AdminControllerTests
{
    private const string MetaUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
    private const string AioUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
    private readonly FakeAioMetadataClient _metadata = new();
    private readonly FakeAioStreamsClient _streams = new();
    private readonly FakeSettings _settings = new();

    private AdminController Create() => new(_metadata, _streams, _settings, taskManager: null!);

    [Fact]
    public async Task Lists_syncable_catalogs_only()
    {
        _metadata.Manifest = new StremioManifest
        {
            Catalogs =
            [
                new StremioCatalog { Type = "movie", Id = "tmdb.top", Name = "Popular" },
                new StremioCatalog { Type = "movie", Id = "search.tmdb", Name = "Search", Extra = [new StremioExtra { Name = "search", IsRequired = true }] },
                new StremioCatalog { Type = "series", Id = "mal.airing" },
            ],
        };

        var result = await Create().GetCatalogs(new ManifestUrlRequest(MetaUrl), CancellationToken.None);

        var options = Assert.IsAssignableFrom<IEnumerable<CatalogOption>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(new[] {new CatalogOption("movie", "tmdb.top", "Popular"), new CatalogOption("series", "mal.airing", "mal.airing")}, options);
    }

    [Fact]
    public async Task Invalid_manifest_url_is_a_bad_request()
    {
        var result = await Create().GetCatalogs(new ManifestUrlRequest("nope"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Test_streams_reports_success_and_failure()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://x.example.com/a.mkv")], []);
        var ok = await Create().TestStreams(new ManifestUrlRequest(AioUrl), CancellationToken.None);
        Assert.Contains("1 stream", Assert.IsType<StatusMessage>(Assert.IsType<OkObjectResult>(ok.Result).Value).Message, StringComparison.Ordinal);

        _streams.Exception = new AioStreamsException("AIOStreams: Invalid password");
        var failed = await Create().TestStreams(new ManifestUrlRequest(AioUrl), CancellationToken.None);
        var error = Assert.IsType<ObjectResult>(failed.Result);
        Assert.Equal(502, error.StatusCode);
        Assert.Contains("Invalid password", Assert.IsType<StatusMessage>(error.Value).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_body_is_a_bad_request()
    {
        Assert.IsType<BadRequestObjectResult>((await Create().GetCatalogs(null, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await Create().TestStreams(null, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Error_messages_never_contain_credentials()
    {
        _streams.Exception = new AioStreamsException("failed for https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/secretpw/manifest.json");

        var result = await Create().TestStreams(new ManifestUrlRequest(AioUrl), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, error.StatusCode);
        var message = Assert.IsType<StatusMessage>(error.Value).Message;
        Assert.DoesNotContain("secretpw", message, StringComparison.Ordinal);
        Assert.DoesNotContain("0b6c3c7e", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Paths_use_the_configured_root()
    {
        _settings.Current.LibraryRoot = Path.Combine(Path.GetTempPath(), "currents-root");

        var paths = Create().GetPaths().Value!;

        Assert.Equal(Path.Combine(Path.GetTempPath(), "currents-root", "Movies"), paths.Movies);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "currents-root", "Shows"), paths.Shows);
    }
}
