using System.Net;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public sealed class SubtitleProxyControllerTests : IDisposable
{
    private const string SubtitleUrl = "https://subs.example.com/file/1?key=SUBSECRET";
    private const string Vtt = "WEBVTT\n\n00:01.000 --> 00:02.000\nHello\n";
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly List<Uri> _fetched = [];
    private string _upstream = Vtt;
    private IPAddress _caller = IPAddress.Loopback;

    public SubtitleProxyControllerTests()
    {
        _settings.Current.AioStreamsManifestUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
        _client.Outcome = new SearchOutcome(
            [new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", Subtitles = [new StremioSubtitle { Id = "1", Url = SubtitleUrl, Lang = "eng" }] }],
            []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SubtitleProxyController Create(string? forwarded = null)
    {
        var http = new DefaultHttpContext { Connection = { RemoteIpAddress = _caller } };
        if (forwarded is not null)
        {
            http.Request.Headers["X-Forwarded-For"] = forwarded;
        }

        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        var downloader = new SubtitleDownloader(new FakeHttpClientFactory(new StubHttpHandler(r =>
        {
            _fetched.Add(r.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_upstream, Encoding.UTF8) };
        })));
        return new SubtitleProxyController(
            new StreamService(_client, _settings, _time, NullLogger<StreamService>.Instance),
            new StreamProfileResolver(users, _settings),
            downloader,
            _settings,
            _time,
            new LocalCallerPolicy(() => []))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private string File(string subtitleUrl = SubtitleUrl)
    {
        var ticket = new VersionTicket(Guid.Empty, "movie", "tt1", StreamIdentity.Keys(_client.Outcome.Results)[0]);
        return new VersionTokenSigner(_settings.Current.SigningSecret, _time).CreateSubtitle(ticket, StreamSubtitles.Key(subtitleUrl), TimeSpan.FromHours(1)) + ".srt";
    }

    [Fact]
    public async Task The_stream_subtitle_is_fetched_and_served_as_srt()
    {
        var result = await Create().GetSubtitle(File(), CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/x-subrip; charset=utf-8", file.ContentType);
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\nHello\n\n", Encoding.UTF8.GetString(file.FileContents));
        Assert.Equal(new Uri(SubtitleUrl), Assert.Single(_fetched));
    }

    [Fact]
    public async Task Remote_callers_forwarded_requests_and_bad_tokens_are_forbidden()
    {
        var forwarded = await Create("203.0.113.9").GetSubtitle(File(), CancellationToken.None);
        var bad = await Create().GetSubtitle("x.y.srt", CancellationToken.None);
        var noExtension = await Create().GetSubtitle(File()[..^4], CancellationToken.None);
        _caller = IPAddress.Parse("203.0.113.9");
        var remote = await Create().GetSubtitle(File(), CancellationToken.None);

        Assert.All(new[] { forwarded, bad, noExtension, remote }, r => Assert.Equal(403, Assert.IsType<StatusCodeResult>(r).StatusCode));
        Assert.Empty(_fetched);
    }

    [Fact]
    public async Task Switched_off_subtitles_are_not_found_and_nothing_is_fetched()
    {
        _settings.Current.EnableSubtitles = false;

        Assert.IsType<NotFoundResult>(await Create().GetSubtitle(File(), CancellationToken.None));
        Assert.Empty(_fetched);
    }

    [Fact]
    public async Task A_subtitle_the_stream_no_longer_offers_is_not_found()
    {
        Assert.IsType<NotFoundResult>(await Create().GetSubtitle(File("https://subs.example.com/gone"), CancellationToken.None));
    }

    [Fact]
    public async Task An_unreadable_upstream_file_is_a_bad_gateway()
    {
        _upstream = "<html>rate limited</html>";

        Assert.Equal(502, Assert.IsType<StatusCodeResult>(await Create().GetSubtitle(File(), CancellationToken.None)).StatusCode);
    }
}
