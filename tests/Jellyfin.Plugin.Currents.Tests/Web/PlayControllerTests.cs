using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public class PlayControllerTests
{
    private readonly FakeSettings _settings = new();
    private readonly FakeResolver _resolver = new();

    private PlayController Create() => new(_resolver, _settings);

    private string Sign(string type, string id) => new StrmSigner(_settings.Current.SigningSecret).Sign(type, id);

    [Fact]
    public async Task Valid_signature_redirects_to_the_resolved_stream()
    {
        _resolver.Result = new ResolveResult(new Uri("https://cdn.example.com/f.mkv"), null);

        var result = await Create().Play("series", "tt0944947:1:2", Sign("series", "tt0944947:1:2"), CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://cdn.example.com/f.mkv", redirect.Url);
        Assert.Equal(("series", "tt0944947:1:2"), _resolver.LastRequest);
    }

    [Fact]
    public async Task Bad_or_missing_signature_is_forbidden_and_does_not_resolve()
    {
        var bad = await Create().Play("movie", "tt1", Sign("movie", "tt2"), CancellationToken.None);
        var missing = await Create().Play("movie", "tt1", null, CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(bad).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(missing).StatusCode);
        Assert.Null(_resolver.LastRequest);
    }

    [Fact]
    public async Task Unknown_type_is_not_found()
    {
        var result = await Create().Play("channel", "x", Sign("channel", "x"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Resolve_failure_returns_503_with_reason()
    {
        _resolver.Result = ResolveResult.Fail("No streams found for this title.");

        var result = await Create().Play("movie", "tt1", Sign("movie", "tt1"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("No streams found for this title.", objectResult.Value);
    }

    private sealed class FakeResolver : IStreamResolver
    {
        public ResolveResult Result { get; set; } = ResolveResult.Fail("unset");

        public (string Type, string Id)? LastRequest { get; private set; }

        public Task<ResolveResult> ResolveAsync(string type, string stremioId, CancellationToken cancellationToken)
        {
            LastRequest = (type, stremioId);
            return Task.FromResult(Result);
        }

        public VersionTicket? LastTicket { get; private set; }

        public Task<ResolveResult> ResolveAsync(VersionTicket ticket, CancellationToken cancellationToken)
        {
            LastTicket = ticket;
            return Task.FromResult(Result);
        }
    }
}
