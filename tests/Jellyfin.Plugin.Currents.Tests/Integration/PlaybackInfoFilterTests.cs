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
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class PlaybackInfoFilterTests : IDisposable
{
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly (IMediaSourceManager Instance, InterfaceFake Fake) _media = InterfaceFake.Create<IMediaSourceManager>();
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly Movie _movie;

    public PlaybackInfoFilterTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M", "M.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm, RunTimeTicks = TimeSpan.FromHours(2).Ticks };
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => (Guid)args[0]! == _movie.Id ? _movie : null);

        _client.Outcome = new SearchOutcome([Described("a"), Described("b"), Described("c")], []);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(new StreamService(_client, _settings, new Jellyfin.Plugin.Currents.Common.DiagnosticsLog(_time), _time, NullLogger<StreamService>.Instance), new StreamProfileResolver(users, _settings), _registry, _settings, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static StreamResult Described(string name) => new()
    {
        Url = $"https://aio.example.com/play/{name}",
        Filename = $"{name}.mkv",
        Size = 4_000_000_000,
        Duration = 7_200_000,
        ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"] },
    };

    private PlaybackInfoFilter Create(Guid user)
    {
        var probes = new ProbeCache(_settings, _time);
        var builder = new VersionSourceBuilder(_settings, _time, probes, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
        var prober = new VersionProber(_media.Instance, _library.Instance, probes, builder, new FixedUrl("http://127.0.0.1:8096"), _time, NullLogger<VersionProber>.Instance);
        var request = RequestContextTests.Create(RequestContextTests.Http(user, action: ("MediaInfo", "GetPostedPlaybackInfo")));
        return new PlaybackInfoFilter(_library.Instance, new CurrentsItemLocator(_settings, _time), _catalog, _registry, prober, builder, request, _settings);
    }

    private static Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext PlaybackInfo(Guid itemId, string? mediaSourceId, FakePlaybackInfoDto? dto = null) =>
        SyntheticVersionIdFilterTests.Context("POST", "MediaInfo", "GetPostedPlaybackInfo", new()
        {
            ["itemId"] = itemId,
            ["mediaSourceId"] = mediaSourceId,
            ["playbackInfoDto"] = dto,
        });

    private async Task<IReadOnlyList<VersionEntry>> AliceVersions() =>
        (await _catalog.GetAsync(_movie.Id, new CurrentsTitle("movie", "tt1"), Alice, TimeSpan.FromSeconds(10), CancellationToken.None)).Versions;

    [Fact]
    public async Task Exact_version_id_is_kept()
    {
        var versions = await AliceVersions();
        var context = PlaybackInfo(_movie.Id, versions[1].VersionId);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[1].VersionId, context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Item_id_or_unknown_id_becomes_the_top_version_in_query_and_body()
    {
        var versions = await AliceVersions();
        var dto = new FakePlaybackInfoDto { MediaSourceId = _movie.Id.ToString("N") };
        var context = PlaybackInfo(_movie.Id, null, dto);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[0].VersionId, context.ActionArguments["mediaSourceId"]);
        Assert.Equal(versions[0].VersionId, dto.MediaSourceId);
    }

    [Fact]
    public async Task Stale_version_id_keeps_its_stream_when_still_offered()
    {
        var versions = await AliceVersions();
        var stale = new VersionEntry(Guid.NewGuid().ToString("N"), _movie.Id, Alice, versions[2].Title, versions[2].Stream);
        _registry.Register(Guid.NewGuid(), Alice, [stale]);
        var context = PlaybackInfo(_movie.Id, stale.VersionId);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[2].VersionId, context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task No_requested_version_is_left_for_jellyfin_to_choose()
    {
        var context = PlaybackInfo(_movie.Id, null);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Null(context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Unconfigured_user_and_other_items_are_untouched()
    {
        var bob = PlaybackInfo(_movie.Id, "x");
        var other = PlaybackInfo(Guid.NewGuid(), "y");

        await SyntheticVersionIdFilterTests.Run(Create(Bob), bob);
        await SyntheticVersionIdFilterTests.Run(Create(Alice), other);

        Assert.Equal("x", bob.ActionArguments["mediaSourceId"]);
        Assert.Equal("y", other.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Other_actions_are_ignored()
    {
        var context = SyntheticVersionIdFilterTests.Context("GET", "UserLibrary", "GetItem", new() { ["itemId"] = _movie.Id });

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.GetItemById)));
    }

    private static StreamResult Bilingual() => new()
    {
        Url = "https://aio.example.com/play/bi",
        Filename = "bi.mkv",
        Size = 4_000_000_000,
        Duration = 7_200_000,
        ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["DD+"], AudioChannels = ["5.1"], Languages = ["English", "French"] },
    };

    private static Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext WithTracks(Guid itemId, string mediaSourceId, int? audio, int? subtitle, FakePlaybackInfoDto dto) =>
        SyntheticVersionIdFilterTests.Context("POST", "MediaInfo", "GetPostedPlaybackInfo", new()
        {
            ["itemId"] = itemId,
            ["mediaSourceId"] = mediaSourceId,
            ["audioStreamIndex"] = audio,
            ["subtitleStreamIndex"] = subtitle,
            ["playbackInfoDto"] = dto,
        });

    private void ProbeFindsFrenchFirst() =>
        _media.Fake.On(nameof(IMediaSourceManager.AddMediaInfoWithProbe), args =>
        {
            var source = (MediaSourceInfo)args[0]!;
            source.MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "ac3", Language = "fre" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 2, Codec = "eac3", Language = "eng" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 3, Codec = "subrip", Language = "eng" },
            ];
            return Task.CompletedTask;
        });

    [Fact]
    public async Task Synthetic_choices_are_mapped_to_the_probed_tracks_in_query_and_body()
    {
        _client.Outcome = new SearchOutcome([Bilingual()], []);
        ProbeFindsFrenchFirst();
        var version = (await AliceVersions())[0];
        var dto = new FakePlaybackInfoDto { AudioStreamIndex = 502 };
        var context = WithTracks(_movie.Id, version.VersionId, 502, null, dto);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(1, context.ActionArguments["audioStreamIndex"]);
        Assert.Equal(1, dto.AudioStreamIndex);
    }

    [Fact]
    public async Task A_stale_page_choice_still_maps_after_the_version_has_been_probed()
    {
        _client.Outcome = new SearchOutcome([Bilingual()], []);
        ProbeFindsFrenchFirst();
        var version = (await AliceVersions())[0];
        var filter = Create(Alice);
        var first = WithTracks(_movie.Id, version.VersionId, 502, null, new FakePlaybackInfoDto { AudioStreamIndex = 502 });
        var second = WithTracks(_movie.Id, version.VersionId, 502, null, new FakePlaybackInfoDto { AudioStreamIndex = 502 });

        await SyntheticVersionIdFilterTests.Run(filter, first);
        await SyntheticVersionIdFilterTests.Run(filter, second);

        Assert.Equal(1, first.ActionArguments["audioStreamIndex"]);
        Assert.Equal(1, second.ActionArguments["audioStreamIndex"]);
    }

    [Fact]
    public async Task A_synthetic_subtitle_choice_maps_to_the_probed_subtitle()
    {
        var release = Bilingual();
        release.ParsedFile!.Subtitles = ["English"];
        _client.Outcome = new SearchOutcome([release], []);
        ProbeFindsFrenchFirst();
        var version = (await AliceVersions())[0];
        var dto = new FakePlaybackInfoDto { SubtitleStreamIndex = 503 };
        var context = WithTracks(_movie.Id, version.VersionId, null, 503, dto);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(3, context.ActionArguments["subtitleStreamIndex"]);
        Assert.Equal(3, dto.SubtitleStreamIndex);
    }

    [Fact]
    public async Task The_default_synthetic_audio_needs_no_probe()
    {
        _client.Outcome = new SearchOutcome([new StreamResult { Url = "https://aio.example.com/play/one", Filename = "one.mkv", Size = 4_000_000_000, Duration = 7_200_000, ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"], AudioChannels = ["2.0"], Languages = ["English"] } }], []);
        var version = (await AliceVersions())[0];
        var context = WithTracks(_movie.Id, version.VersionId, 501, null, new FakePlaybackInfoDto());

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Null(context.ActionArguments["audioStreamIndex"]);
        Assert.Empty(_media.Fake.Calls(nameof(IMediaSourceManager.AddMediaInfoWithProbe)));
    }

    [Fact]
    public async Task Synthetic_choice_without_a_probe_is_cleared()
    {
        _client.Outcome = new SearchOutcome([Bilingual()], []);
        _media.Fake.On(nameof(IMediaSourceManager.AddMediaInfoWithProbe), _ => Task.FromException(new InvalidOperationException("ffprobe failed")));
        var version = (await AliceVersions())[0];
        var dto = new FakePlaybackInfoDto { AudioStreamIndex = 502, SubtitleStreamIndex = 503 };
        var context = WithTracks(_movie.Id, version.VersionId, null, null, dto);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Null(dto.AudioStreamIndex);
        Assert.Null(dto.SubtitleStreamIndex);
    }

    [Fact]
    public async Task Real_external_and_off_indexes_pass_through()
    {
        _client.Outcome = new SearchOutcome([Bilingual()], []);
        ProbeFindsFrenchFirst();
        var version = (await AliceVersions())[0];
        var context = WithTracks(_movie.Id, version.VersionId, 2, 1000, new FakePlaybackInfoDto());
        var off = WithTracks(_movie.Id, version.VersionId, 2, -1, new FakePlaybackInfoDto());

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);
        await SyntheticVersionIdFilterTests.Run(Create(Alice), off);

        Assert.Equal((2, 1000), ((int)context.ActionArguments["audioStreamIndex"]!, (int)context.ActionArguments["subtitleStreamIndex"]!));
        Assert.Equal(-1, off.ActionArguments["subtitleStreamIndex"]);
    }

    [Fact]
    public async Task Loopback_subtitle_urls_in_playback_info_are_rewritten()
    {
        var version = (await AliceVersions())[0];
        var context = WithTracks(_movie.Id, version.VersionId, null, null, new FakePlaybackInfoDto());
        var subtitle = new MediaStream
        {
            Type = MediaStreamType.Subtitle,
            Index = 1000,
            Codec = "srt",
            IsExternal = true,
            Path = "http://127.0.0.1:8096/Currents/subtitles/TOKEN.srt",
            DeliveryUrl = "http://127.0.0.1:8096/Currents/subtitles/TOKEN.srt",
            IsExternalUrl = true,
        };
        var source = new MediaSourceInfo { Id = version.VersionId, Path = "http://127.0.0.1:8096/Currents/play/s/TOKEN", MediaStreams = [subtitle] };
        var executed = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext(context, [], new object())
        {
            Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new MediaBrowser.Model.MediaInfo.PlaybackInfoResponse { MediaSources = [source] }),
        };

        await Create(Alice).OnActionExecutionAsync(context, () => Task.FromResult(executed));

        Assert.Equal($"/Videos/{_movie.Id:N}/{version.VersionId}/Subtitles/1000/0/Stream.srt", subtitle.DeliveryUrl);
        Assert.False(subtitle.IsExternalUrl);
        Assert.DoesNotContain("/Currents/", subtitle.Path ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal($"currents://version/{version.VersionId}", source.Path);
    }

    public sealed class FakePlaybackInfoDto
    {
        public string? MediaSourceId { get; set; }

        public int? AudioStreamIndex { get; set; }

        public int? SubtitleStreamIndex { get; set; }
    }

    private sealed class FixedUrl(string value) : IInternalBaseUrl
    {
        public string Value => value;
    }
}
