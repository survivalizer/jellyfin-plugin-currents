using System.Net;
using System.Reflection;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public class PlayControllerTests
{
    private readonly FakeSettings _settings = new();
    private readonly FakeResolver _resolver = new();

    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private IPAddress _caller = IPAddress.Loopback;

    private PlayController Create() =>
        new(_resolver, _settings, _time, new LocalCallerPolicy(() => []))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = _caller } } },
        };

    private string Token(VersionTicket ticket, TimeSpan? lifetime = null) =>
        new VersionTokenSigner(_settings.Current.SigningSecret, _time).Create(ticket, lifetime ?? TimeSpan.FromHours(24));

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

    [Fact]
    public async Task Valid_version_token_from_the_server_resolves_that_ticket()
    {
        _resolver.Result = new ResolveResult(new Uri("https://cdn.example.com/v.mkv"), null);
        var ticket = new VersionTicket(Guid.NewGuid(), "movie", "tt1", "0123456789abcdef0123456789abcdef");

        var result = await Create().PlayVersion(Token(ticket), CancellationToken.None);

        Assert.Equal("https://cdn.example.com/v.mkv", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal(ticket, _resolver.LastTicket);
    }

    [Fact]
    public async Task Version_token_from_a_remote_caller_is_forbidden()
    {
        _caller = IPAddress.Parse("203.0.113.9");

        var result = await Create().PlayVersion(Token(new VersionTicket(Guid.NewGuid(), "movie", "tt1", "k")), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Null(_resolver.LastTicket);
    }

    [Fact]
    public async Task Expired_or_forged_version_token_is_forbidden()
    {
        var token = Token(new VersionTicket(Guid.NewGuid(), "movie", "tt1", "k"), TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(2));

        var expired = await Create().PlayVersion(token, CancellationToken.None);
        var forged = await Create().PlayVersion("e30.AAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(expired).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(forged).StatusCode);
        Assert.Null(_resolver.LastTicket);
    }

    [Fact]
    public async Task Version_resolve_failure_returns_503_with_reason()
    {
        _resolver.Result = ResolveResult.Fail("Streams are disabled for your account.");

        var result = await Create().PlayVersion(Token(new VersionTicket(Guid.NewGuid(), "movie", "tt1", "k")), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("Streams are disabled for your account.", objectResult.Value);
    }

    [Fact]
    public void Version_route_matches_the_signer_path()
    {
        var controllerRoute = typeof(PlayController).GetCustomAttribute<RouteAttribute>()!.Template;
        var actionRoute = typeof(PlayController).GetMethod(nameof(PlayController.PlayVersion))!.GetCustomAttribute<HttpGetAttribute>()!.Template;

        Assert.Equal(VersionTokenSigner.PathFor("{token}"), $"{controllerRoute}/{actionRoute}");
    }

    [Fact]
    public async Task Strm_route_still_works_for_remote_clients()
    {
        _caller = IPAddress.Parse("203.0.113.9");
        _resolver.Result = new ResolveResult(new Uri("https://cdn.example.com/f.mkv"), null);

        var result = await Create().Play("movie", "tt1", Sign("movie", "tt1"), CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
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
