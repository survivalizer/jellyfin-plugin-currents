using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Features.Subtitles;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.Integration;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Features;

public sealed class CurrentsSubtitleProviderTests : IDisposable
{
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly string _strm;
    private string _download = "1\n00:00:01,000 --> 00:00:02,000\nHi\n";

    public CurrentsSubtitleProviderTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _strm = Path.Combine(_settings.Current.LibraryRoot, "Shows", "S", "Season 01", "S S01E02.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(_strm)!);
        File.WriteAllText(_strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "series", "tt1:1:2"));
        _client.SubtitleList =
        [
            new StremioSubtitle { Id = "a", Url = "https://subs.example.com/a?key=SUBSECRET", Lang = "eng" },
            new StremioSubtitle { Id = "b", Url = "https://subs.example.com/b", Lang = "fre" },
            new StremioSubtitle { Id = "error.X", Url = "https://github.com/Viren070/AIOStreams", Lang = "[❌] X - failed" },
        ];
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private CurrentsSubtitleProvider Create(Guid? user = null)
    {
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        var downloader = new SubtitleDownloader(new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_download, Encoding.UTF8) })));
        return new CurrentsSubtitleProvider(
            new CurrentsItemLocator(_settings, _time),
            new StreamProfileResolver(users, _settings),
            RequestContextTests.Create(RequestContextTests.Http(user ?? Alice, action: ("Subtitle", "SearchRemoteSubtitles"))),
            _client,
            downloader,
            _settings,
            _time,
            NullLogger<CurrentsSubtitleProvider>.Instance);
    }

    private SubtitleSearchRequest Request(string language = "eng", bool automated = false) =>
        new() { MediaPath = _strm, Language = language, ContentType = VideoContentType.Episode, IsAutomated = automated };

    [Fact]
    public async Task Searches_the_users_config_for_the_episode_and_filters_by_language()
    {
        var results = (await Create().Search(Request("eng"), CancellationToken.None)).ToList();

        var result = Assert.Single(results);
        Assert.Equal("Currents (AIOStreams)", result.ProviderName);
        Assert.Equal("eng", result.ThreeLetterISOLanguageName);
        Assert.Equal("alice", _client.LastCredentials!.Password);
        Assert.True(SubtitleSearchId.TryDecode(result.Id, out var title, out var key));
        Assert.Equal(new CurrentsTitle("series", "tt1:1:2"), title);
        Assert.Equal(StreamSubtitles.Key("https://subs.example.com/a?key=SUBSECRET"), key);
    }

    [Fact]
    public async Task Search_results_carry_no_urls()
    {
        var json = JsonSerializer.Serialize(await Create().Search(Request("eng"), CancellationToken.None));

        Assert.DoesNotContain("subs.example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBSECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Automated_searches_other_items_and_switched_off_return_nothing()
    {
        var automated = await Create().Search(Request(automated: true), CancellationToken.None);
        var other = await Create().Search(new SubtitleSearchRequest { MediaPath = "/media/Movie.mkv", Language = "eng" }, CancellationToken.None);
        _settings.Current.EnableSubtitles = false;
        var off = await Create().Search(Request(), CancellationToken.None);

        Assert.Empty(automated);
        Assert.Empty(other);
        Assert.Empty(off);
        Assert.Equal(0, _client.SubtitleCalls);
    }

    [Fact]
    public async Task Lists_are_cached_and_failures_return_nothing_for_two_minutes()
    {
        var provider = Create();
        await provider.Search(Request(), CancellationToken.None);
        await provider.Search(Request("fre"), CancellationToken.None);
        Assert.Equal(1, _client.SubtitleCalls);

        var failing = Create();
        _client.SubtitleException = new AioStreamsException("AIOStreams returned 500 for https://aio.example.com/stremio/***/***/subtitles/series/tt1.json.");
        _time.Advance(TimeSpan.FromHours(2));
        Assert.Empty(await failing.Search(Request(), CancellationToken.None));
        Assert.Empty(await failing.Search(Request(), CancellationToken.None));
        Assert.Equal(2, _client.SubtitleCalls);
    }

    [Fact]
    public async Task Download_returns_the_file_in_its_own_format_and_language()
    {
        var provider = Create();
        var id = (await provider.Search(Request("fre"), CancellationToken.None)).Single().Id;
        _download = "WEBVTT\n\n00:01.000 --> 00:02.000\nSalut\n";

        var response = await provider.GetSubtitles(id, CancellationToken.None);

        Assert.Equal(("vtt", "fre"), (response.Format, response.Language));
        using var reader = new StreamReader(response.Stream);
        Assert.StartsWith("WEBVTT", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_or_stale_ids_throw()
    {
        var provider = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetSubtitles("not-an-id", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSubtitles(SubtitleSearchId.Encode(new CurrentsTitle("series", "tt1:1:2"), "0000000000000000"), CancellationToken.None));
    }
}
