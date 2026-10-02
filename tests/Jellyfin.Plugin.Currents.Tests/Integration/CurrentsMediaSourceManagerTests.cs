using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class CurrentsMediaSourceManagerTests : IDisposable
{
    private const string Internal = "http://127.0.0.1:8096";
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly (string, string) ItemPage = ("UserLibrary", "GetItem");
    private static readonly (string, string) PlaybackInfo = ("MediaInfo", "GetPostedPlaybackInfo");
    private static readonly (string, string) ListView = ("Items", "GetItems");
    private static readonly (string, string) Stream = ("DynamicHls", "GetMasterHlsVideoPlaylist");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (IDisposableMediaSourceManager Instance, InterfaceFake Fake) _inner = InterfaceFake.Create<IDisposableMediaSourceManager>();
    private readonly List<MediaSourceInfo> _innerSources = [new MediaSourceInfo { Id = "inner" }];
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly VersionSourceBuilder _builder;
    private readonly CurrentsItemLocator _locator;
    private readonly Movie _movie;

    public CurrentsMediaSourceManagerTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M (2020)", "M (2020).strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://192.168.1.5:8097", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm, Name = "M" };

        _client.Outcome = new SearchOutcome(
            [
                new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", ParsedFile = new ParsedFile { Resolution = "2160p", Encode = "HEVC" } },
                new StreamResult { Url = "https://aio.example.com/play/2", Filename = "b.mkv", ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } },
            ],
            []);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(new StreamService(_client, _settings, _time, NullLogger<StreamService>.Instance), new StreamProfileResolver(users, _settings), _registry, _settings);
        _builder = new VersionSourceBuilder(_settings, _time, new ProbeCache(_time));
        _locator = new CurrentsItemLocator(_settings, _time);
        _inner.Fake.On(nameof(IMediaSourceManager.GetStaticMediaSources), _ => _innerSources);
        _inner.Fake.On(nameof(IMediaSourceManager.GetPlaybackMediaSources), _ => Task.FromResult<IReadOnlyList<MediaSourceInfo>>(_innerSources));
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private CurrentsMediaSourceManager Create(HttpContext? http) =>
        new(_inner.Instance, _locator, _catalog, _registry, _builder, RequestContextTests.Create(http), new FixedInternalBaseUrl(Internal), _settings);

    private static HttpContext Request(Guid? user, (string, string) action) => RequestContextTests.Http(user, action: action);

    [Fact]
    public void Other_items_pass_through()
    {
        var other = new Movie { Path = Path.Combine(_settings.DataFolderPath, "elsewhere.mkv") };

        Assert.Same(_innerSources, Create(Request(Alice, ItemPage)).GetStaticMediaSources(other, true));
    }

    [Fact]
    public async Task Versions_switched_off_pass_currents_items_through()
    {
        _settings.Current.EnableVersions = false;

        var manager = Create(Request(Alice, ItemPage));

        Assert.Same(_innerSources, manager.GetStaticMediaSources(_movie, true));
        Assert.Same(_innerSources, await manager.GetPlaybackMediaSources(_movie, null!, true, true, CancellationToken.None));
    }

    [Fact]
    public void Item_page_lists_the_users_versions_with_redacted_paths()
    {
        var sources = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        Assert.Equal(2, sources.Count);
        Assert.All(sources, s => Assert.StartsWith("currents://version/", s.Path, StringComparison.Ordinal));
        Assert.All(sources, s => Assert.DoesNotContain("aio.example.com", s.Path, StringComparison.Ordinal));
        Assert.True(_registry.TryGet(sources[0].Id, out var entry));
        Assert.Equal(Alice, entry.UserId);
        Assert.StartsWith("2160p", sources[0].Name, StringComparison.Ordinal);
        Assert.Equal(1, _client.Calls);
        Assert.Empty(_inner.Fake.Calls(nameof(IMediaSourceManager.GetStaticMediaSources)));
    }

    [Fact]
    public void Playback_info_may_search()
    {
        var sources = Create(Request(Alice, PlaybackInfo)).GetStaticMediaSources(_movie, true);

        Assert.Equal(2, sources.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public void List_views_never_search()
    {
        var cold = Create(Request(Alice, ListView)).GetStaticMediaSources(_movie, true);
        Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);
        var warm = Create(Request(Alice, ListView)).GetStaticMediaSources(_movie, true);

        Assert.Equal(_movie.Id.ToString("N"), Assert.Single(cold).Id);
        Assert.False(cold[0].SupportsTranscoding);
        Assert.Equal(2, warm.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public void Unconfigured_user_sees_a_single_notice()
    {
        var sources = Create(Request(Bob, ItemPage)).GetStaticMediaSources(_movie, true);

        var notice = Assert.Single(sources);
        Assert.Contains("not configured", notice.Name, StringComparison.OrdinalIgnoreCase);
        Assert.False(notice.SupportsDirectPlay || notice.SupportsDirectStream || notice.SupportsTranscoding);
    }

    [Fact]
    public async Task Streaming_uses_the_token_user_and_internal_paths()
    {
        var sources = await Create(Request(Alice, Stream)).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);

        Assert.Equal(2, sources.Count);
        var token = sources[0].Path[(Internal.Length + "/Currents/play/s/".Length)..];
        Assert.StartsWith($"{Internal}/Currents/play/s/", sources[0].Path, StringComparison.Ordinal);
        Assert.True(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out var ticket));
        Assert.Equal(Alice, ticket.UserId);
        Assert.Empty(_inner.Fake.Calls(nameof(IMediaSourceManager.GetPlaybackMediaSources)));
    }

    [Fact]
    public async Task Background_calls_without_a_request_use_the_registry()
    {
        var page = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        var background = await Create(null).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);

        Assert.Equal(page.Select(s => s.Id), background.Select(s => s.Id));
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Get_media_source_finds_versions_by_id_without_a_user()
    {
        var page = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);
        var background = Create(null);

        var byVersion = await background.GetMediaSource(_movie, page[1].Id, null!, false, CancellationToken.None);
        var byItem = await background.GetMediaSource(_movie, _movie.Id.ToString("N"), null!, false, CancellationToken.None);
        var unknown = await background.GetMediaSource(_movie, Guid.NewGuid().ToString("N"), null!, false, CancellationToken.None);

        Assert.Equal(page[1].Id, byVersion!.Id);
        Assert.StartsWith(Internal, byVersion!.Path, StringComparison.Ordinal);
        Assert.Equal(page[0].Id, byItem!.Id);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Another_users_version_id_is_refused()
    {
        var alice = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        var asBob = await Create(Request(Bob, Stream)).GetMediaSource(_movie, alice[0].Id, null!, false, CancellationToken.None);
        var bobsList = await Create(Request(Bob, Stream)).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);

        Assert.Null(asBob);
        Assert.DoesNotContain(bobsList, s => s.Id == alice[0].Id);
    }

    [Fact]
    public async Task Anonymous_requests_never_get_another_users_versions()
    {
        var alice = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        var anonymous = await Create(Request(null, Stream)).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);
        var byId = await Create(Request(null, Stream)).GetMediaSource(_movie, alice[0].Id, null!, false, CancellationToken.None);

        Assert.Equal(_movie.Id.ToString("N"), Assert.Single(anonymous).Id);
        Assert.Equal("currents://pending", anonymous[0].Path);
        Assert.Null(byId);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Anonymous_requests_may_use_default_config_versions()
    {
        _settings.Current.AioStreamsManifestUrl = AliceUrl;
        var defaults = Create(Request(null, ItemPage)).GetStaticMediaSources(_movie, true);
        Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        var anonymous = await Create(Request(null, Stream)).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);
        var byId = await Create(Request(null, Stream)).GetMediaSource(_movie, defaults[0].Id, null!, false, CancellationToken.None);

        Assert.Equal(defaults.Select(s => s.Id), anonymous.Select(s => s.Id));
        Assert.Equal(defaults[0].Id, byId!.Id);
    }

    [Fact]
    public void Every_call_returns_new_objects()
    {
        var manager = Create(Request(Alice, ItemPage));

        var first = manager.GetStaticMediaSources(_movie, true);
        var second = manager.GetStaticMediaSources(_movie, true);

        Assert.NotSame(first[0], second[0]);
        Assert.NotSame(first[0].MediaStreams[0], second[0].MediaStreams[0]);
    }

    [Fact]
    public void Dispose_disposes_jellyfins_manager()
    {
        Create(null).Dispose();

        Assert.Single(_inner.Fake.Calls(nameof(IDisposable.Dispose)));
    }

    private sealed class FixedInternalBaseUrl(string value) : IInternalBaseUrl
    {
        public string Value => value;
    }
}
