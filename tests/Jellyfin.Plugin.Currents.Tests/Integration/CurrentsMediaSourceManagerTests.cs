using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
    private CompatState _compat;
    private readonly FakeAioStreamsClient _client = new();
    private readonly UserStore _users;
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (IDisposableMediaSourceManager Instance, InterfaceFake Fake) _inner = InterfaceFake.Create<IDisposableMediaSourceManager>();
    private readonly List<MediaSourceInfo> _innerSources = [new MediaSourceInfo { Id = "inner" }];
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly VersionSourceBuilder _builder;
    private readonly ProbeCache _probes;
    private readonly ListLogger<CurrentsMediaSourceManager> _logger = new();
    private readonly CurrentsItemLocator _locator;
    private readonly Movie _movie;
    private readonly SegmentStore _segmentStore;
    private readonly (IMediaSegmentManager Instance, InterfaceFake Fake) _segmentManager = InterfaceFake.Create<IMediaSegmentManager>();
    private readonly ServiceProvider _segmentServices;
    private bool _storedSegments = true;

    public CurrentsMediaSourceManagerTests()
    {
        _compat = new CompatState(new Version(12, 1, 0), _settings);
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
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(new StreamService(_client, _settings, new Jellyfin.Plugin.Currents.Common.DiagnosticsLog(_time), _time, NullLogger<StreamService>.Instance), new StreamProfileResolver(_users, _settings), _registry, _settings, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
        _probes = new ProbeCache(_settings, _time);
        _builder = new VersionSourceBuilder(_settings, _time, _probes, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
        _locator = new CurrentsItemLocator(_settings, _time);
        _inner.Fake.On(nameof(IMediaSourceManager.GetStaticMediaSources), _ => _innerSources);
        _inner.Fake.On(nameof(IMediaSourceManager.GetPlaybackMediaSources), _ => Task.FromResult<IReadOnlyList<MediaSourceInfo>>(_innerSources));
        _inner.Fake.On(nameof(IMediaSourceManager.GetMediaStreams), _ => (IReadOnlyList<MediaStream>)[]);
        _segmentStore = new SegmentStore(_settings, _time);
        _segmentManager.Fake.On(nameof(IMediaSegmentManager.HasSegments), _ => _storedSegments);
        _segmentServices = new ServiceCollection().AddSingleton(_segmentManager.Instance).BuildServiceProvider();
    }

    public void Dispose()
    {
        _segmentServices.Dispose();
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private CurrentsMediaSourceManager Create(HttpContext? http) =>
        new(_inner.Instance, _locator, _catalog, _registry, _builder, new TrackLocalizer(InterfaceFake.Create<ILocalizationManager>().Instance), RequestContextTests.Create(http), new FixedInternalBaseUrl(Internal), new SegmentGate(_segmentStore, _settings), new SegmentPresence(_segmentServices), _compat, _settings, _logger);

    private static HttpContext Request(Guid? user, (string, string) action) => RequestContextTests.Http(user, action: action);

    [Fact]
    public async Task Item_pages_get_display_tracks_and_playback_gets_stubs()
    {
        _client.Outcome = new SearchOutcome(
            [new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", Languages = ["English", "French"] } }],
            []);

        var page = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);
        var playback = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);
        var byId = await Create(null).GetMediaSource(_movie, playback[0].Id, null!, false, CancellationToken.None);

        Assert.Equal(new[] { 500, 501, 502 }, page[0].MediaStreams.Select(s => s.Index));
        Assert.All(playback[0].MediaStreams, s => Assert.Equal(-1, s.Index));
        Assert.All(byId!.MediaStreams, s => Assert.Equal(-1, s.Index));
    }

    [Fact]
    public void Other_items_pass_through()
    {
        var other = new Movie { Path = Path.Combine(_settings.DataFolderPath, "elsewhere.mkv") };

        Assert.Same(_innerSources, Create(Request(Alice, ItemPage)).GetStaticMediaSources(other, true));
    }

    [Fact]
    public async Task An_untested_server_passes_currents_items_through_until_forced()
    {
        _compat = new CompatState(new Version(13, 0, 0), _settings);
        var manager = Create(Request(Alice, ItemPage));

        Assert.Same(_innerSources, manager.GetStaticMediaSources(_movie, true));
        Assert.Same(_innerSources, await manager.GetPlaybackMediaSources(_movie, null!, true, true, CancellationToken.None));

        _settings.Current.ForceEnableOnUntestedServer = true;
        Assert.Equal(2, manager.GetStaticMediaSources(_movie, true).Count);
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
    public void A_version_that_fails_to_build_is_skipped()
    {
        var keys = StreamIdentity.Keys(_client.Outcome.Results);
        _probes.Set(keys[0], new ProbedMedia("not json", "mkv", null, null, null));

        var sources = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        Assert.StartsWith("1080p", Assert.Single(sources).Name, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void When_every_version_fails_to_build_a_notice_is_shown()
    {
        foreach (var key in StreamIdentity.Keys(_client.Outcome.Results))
        {
            _probes.Set(key, new ProbedMedia("not json", "mkv", null, null, null));
        }

        var sources = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);

        Assert.Equal("No playable streams for this title.", Assert.Single(sources).Name);
        Assert.Equal("currents://notice", sources[0].Path);
    }

    [Fact]
    public async Task A_known_user_keeps_registered_versions_when_a_new_search_finds_none()
    {
        var page = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);
        _time.Advance(TimeSpan.FromMinutes(61));
        _client.Outcome = new SearchOutcome([], []);

        var resumed = await Create(Request(Alice, Stream)).GetPlaybackMediaSources(_movie, null!, false, false, CancellationToken.None);

        Assert.Equal(2, _client.Calls);
        Assert.Equal(page.Select(s => s.Id), resumed.Select(s => s.Id));
    }

    [Fact]
    public async Task A_user_whose_streams_were_turned_off_gets_no_stale_versions()
    {
        var before = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);
        Assert.Equal(2, before.Count);

        _users.Update(Alice, r => r.StreamsDisabled = true);
        var after = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);

        Assert.StartsWith("currents://notice", Assert.Single(after).Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_fresh_lookup_still_keeps_a_resumed_version()
    {
        var before = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);
        _client.Outcome = new SearchOutcome([], []);
        _time.Advance(TimeSpan.FromHours(2));

        var after = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);

        Assert.Equal(before.Select(s => s.Id), after.Select(s => s.Id));
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

    [Fact]
    public async Task Downloaded_subtitles_appear_in_every_version()
    {
        _inner.Fake.On(nameof(IMediaSourceManager.GetMediaStreams), args => args[0] is MediaStreamQuery query && query.ItemId == _movie.Id && query.Type == MediaStreamType.Subtitle
            ? (IReadOnlyList<MediaStream>)
            [
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 0, IsExternal = true, Codec = "srt", Language = "eng", Path = "/library/Movies/M (2020)/M (2020).eng.srt" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 1, IsExternal = false, Codec = "subrip" },
            ]
            : []);

        var page = Create(Request(Alice, ItemPage)).GetStaticMediaSources(_movie, true);
        var playback = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);

        Assert.Equal(2, page.Count);
        Assert.All(page.Concat(playback), source => Assert.Single(source.MediaStreams, s => s.Index == 2000 && s.IsExternal && s.SupportsExternalStream && s.Language == "eng"));
        Assert.All(page.Concat(playback), source => Assert.DoesNotContain(source.MediaStreams, s => s.Index == 2001));
        Assert.NotSame(page[0].MediaStreams.Single(s => s.Index == 2000), page[1].MediaStreams.Single(s => s.Index == 2000));
    }

    private void StoreMarkers(double referenceMinutes) =>
        _segmentStore.Set(new CurrentsTitle("movie", "tt1"), new SegmentLookup([new SkipMarker(MarkerKind.Intro, 0, 5_000)], (long)(referenceMinutes * TimeSpan.TicksPerMinute)));

    [Fact]
    public async Task Only_versions_within_the_tolerance_have_segments()
    {
        _client.Outcome = new SearchOutcome(
            [
                new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", Duration = 7_250_000, ParsedFile = new ParsedFile { Resolution = "2160p", Encode = "HEVC" } },
                new StreamResult { Url = "https://aio.example.com/play/2", Filename = "b.mkv", Duration = 6_000_000, ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } },
            ],
            []);
        StoreMarkers(120);

        var playback = await Create(Request(Alice, PlaybackInfo)).GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None);
        var byId = await Create(Request(Alice, PlaybackInfo)).GetMediaSource(_movie, playback[1].Id, null!, false, CancellationToken.None);

        Assert.Equal(new[] { true, false }, playback.Select(s => s.HasSegments));
        Assert.False(byId!.HasSegments);
    }

    [Fact]
    public async Task Version_without_a_real_runtime_gets_no_markers_by_default()
    {
        _movie.RunTimeTicks = TimeSpan.FromMinutes(120).Ticks;
        _client.Outcome = new SearchOutcome([new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } }], []);
        StoreMarkers(120);
        var manager = Create(Request(Alice, PlaybackInfo));

        Assert.False((await manager.GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None))[0].HasSegments);

        _settings.Current.SegmentsWhenRuntimeUnknown = true;
        Assert.True((await manager.GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None))[0].HasSegments);
    }

    [Fact]
    public async Task Markers_off_or_none_stored_means_no_segments()
    {
        _client.Outcome = new SearchOutcome([new StreamResult { Url = "https://aio.example.com/play/1", Filename = "a.mkv", Duration = 7_200_000, ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } }], []);
        StoreMarkers(120);
        var manager = Create(Request(Alice, PlaybackInfo));
        Assert.True((await manager.GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None))[0].HasSegments);

        _settings.Current.EnableSegments = false;
        Assert.False((await manager.GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None))[0].HasSegments);

        _settings.Current.EnableSegments = true;
        _storedSegments = false;
        Assert.False((await manager.GetPlaybackMediaSources(_movie, null, true, false, CancellationToken.None))[0].HasSegments);
    }

    private sealed class FixedInternalBaseUrl(string value) : IInternalBaseUrl
    {
        public string Value => value;
    }
}
