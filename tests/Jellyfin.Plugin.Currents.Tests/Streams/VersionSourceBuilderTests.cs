using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public sealed class VersionSourceBuilderTests : IDisposable
{
    private const string Internal = "http://127.0.0.1:8096/jf";
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly ProbeCache _probes;

    public VersionSourceBuilderTests() => _probes = new ProbeCache(_settings, _time);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static VersionEntry Entry(StreamResult? result = null)
    {
        result ??= new StreamResult
        {
            Url = "https://aio.example.com/api/v1/debrid/playback/SECRET/x.mkv",
            Filename = "Movie.2160p.mkv",
            Size = 18_400_000_000,
            Cached = true,
            Addon = "Torrentio",
            Duration = 7_200_000,
            ParsedFile = new ParsedFile { Resolution = "2160p", Encode = "HEVC", VisualTags = ["HDR10"], AudioTags = ["DD+"], AudioChannels = ["5.1"] },
        };
        const string key = "0123456789abcdef0123456789abcdef";
        return new VersionEntry(StreamIdentity.VersionId(Item, Alice, key, FakeSettings.Secret), Item, Alice, new CurrentsTitle("movie", "tt1"), new RankedStream(key, result));
    }

    private VersionSourceBuilder Create() => new(_settings, _time, _probes, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));

    private static VersionContext Context(bool redact = false, bool remux = true, bool transcode = true, long? itemRuntime = null) =>
        new(Internal, redact, itemRuntime, remux, transcode);

    [Fact]
    public void Display_view_has_synthetic_indexes_and_playback_view_has_stubs()
    {
        var result = Entry().Stream.Result;
        result.ParsedFile!.Languages = ["English", "Japanese"];
        var entry = Entry(result);

        var display = Create().Build(entry, Context(redact: true));
        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 500, 501, 502 }, display.MediaStreams.Select(s => s.Index));
        Assert.Equal(new[] { -1, -1 }, playback.MediaStreams.Select(s => s.Index));
    }

    [Fact]
    public void Client_sources_never_carry_request_headers()
    {
        var result = Entry().Stream.Result;
        result.RequestHeaders = new Dictionary<string, string> { ["Authorization"] = "Basic SECRETHEADER" };

        var source = Create().Build(Entry(result), Context(redact: true));

        Assert.Empty(source.RequiredHttpHeaders);
        Assert.DoesNotContain("SECRETHEADER", System.Text.Json.JsonSerializer.Serialize(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_a_streamed_never_direct_played_http_source()
    {
        var entry = Entry();

        var source = Create().Build(entry, Context());

        Assert.Equal(entry.VersionId, source.Id);
        Assert.Equal("2160p HDR10 · HEVC · DD+ · 18.4 GB · cached · Torrentio", source.Name);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.True(source.IsRemote);
        Assert.False(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
        Assert.True(source.SupportsTranscoding);
        Assert.False(source.RequiresOpening);
        Assert.Equal("mkv", source.Container);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, source.RunTimeTicks);
        Assert.Equal(18_400_000_000, source.Size);
        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio }, source.MediaStreams.Select(s => s.Type));
    }

    [Fact]
    public void Internal_path_carries_a_token_for_this_users_stream()
    {
        var entry = Entry();

        var source = Create().Build(entry, Context());

        Assert.StartsWith($"{Internal}/Currents/play/s/", source.Path, StringComparison.Ordinal);
        var token = source.Path[(Internal.Length + "/Currents/play/s/".Length)..];
        Assert.True(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out var ticket));
        Assert.Equal(entry.Ticket, ticket);
        Assert.DoesNotContain("SECRET", source.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_lives_for_the_configured_hours()
    {
        _settings.Current.VersionTokenHours = 2;
        var source = Create().Build(Entry(), Context());
        var token = source.Path[(Internal.Length + "/Currents/play/s/".Length)..];

        _time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

        Assert.False(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out _));
    }

    [Fact]
    public void Client_facing_sources_have_a_redacted_path()
    {
        var source = Create().Build(Entry(), Context(redact: true));

        Assert.Equal($"currents://version/{Entry().VersionId}", source.Path);
    }

    [Fact]
    public void User_permissions_switch_off_remux_and_transcode()
    {
        var source = Create().Build(Entry(), Context(remux: false, transcode: false));

        Assert.False(source.SupportsDirectStream);
        Assert.False(source.SupportsTranscoding);
    }

    [Fact]
    public void Probe_results_replace_the_prefill()
    {
        var entry = Entry();
        _probes.Set(entry.Stream.Key, ProbedMedia.From(new MediaSourceInfo
        {
            Container = "mp4",
            RunTimeTicks = 999,
            Bitrate = 1234,
            MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "h264" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "aac" },
                new MediaStream { Type = MediaStreamType.Audio, Index = 2, Codec = "ac3", Language = "jpn" },
            ],
        }));

        var source = Create().Build(entry, Context());

        Assert.Equal("mp4", source.Container);
        Assert.Equal(999, source.RunTimeTicks);
        Assert.Equal(1234, source.Bitrate);
        Assert.Equal(new[] { 0, 1, 2 }, source.MediaStreams.Select(s => s.Index));
    }

    [Fact]
    public void Item_runtime_is_the_fallback_runtime()
    {
        var entry = Entry(new StreamResult { Url = "https://x/a", Size = 1_000_000_000, ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC" } });

        var source = Create().Build(entry, Context(itemRuntime: TimeSpan.FromMinutes(42).Ticks));

        Assert.Equal(TimeSpan.FromMinutes(42).Ticks, source.RunTimeTicks);
    }

    [Fact]
    public void Every_call_returns_new_objects()
    {
        var builder = Create();

        var first = builder.Build(Entry(), Context());
        var second = builder.Build(Entry(), Context());

        Assert.NotSame(first, second);
        Assert.NotSame(first.MediaStreams[0], second.MediaStreams[0]);
    }

    [Fact]
    public void Notice_is_named_after_the_reason_and_cannot_play()
    {
        var notice = VersionSourceBuilder.Notice(Item, "Streams are not configured.");

        Assert.Equal(Item.ToString("N"), notice.Id);
        Assert.Equal("Streams are not configured.", notice.Name);
        Assert.False(notice.SupportsDirectPlay || notice.SupportsDirectStream || notice.SupportsTranscoding);
        Assert.Empty(notice.MediaStreams);
    }

    [Fact]
    public void Pending_uses_the_item_id()
    {
        Assert.Equal(Item.ToString("N"), VersionSourceBuilder.Pending(Item).Id);
    }

    private static StreamResult WithSubtitles()
    {
        var result = Entry().Stream.Result;
        result.Subtitles =
        [
            new StremioSubtitle { Id = "1", Url = "https://subs.example.com/file/1?key=SUBSECRET", Lang = "eng" },
            new StremioSubtitle { Id = "2", Url = "https://subs.example.com/file/1?key=SUBSECRET", Lang = "eng" },
            new StremioSubtitle { Id = "error.X", Url = "https://github.com/Viren070/AIOStreams", Lang = "[❌] X - failed" },
            new StremioSubtitle { Id = "3", Url = "https://subs.example.com/file/3", Lang = "Klingon" },
        ];
        return result;
    }

    [Fact]
    public void Stream_subtitles_become_external_srt_tracks_behind_signed_loopback_urls()
    {
        var entry = Entry(WithSubtitles());

        var source = Create().Build(entry, Context() with { ForPlayback = true });

        var subtitles = source.MediaStreams.Where(s => s.Type == MediaStreamType.Subtitle).ToList();
        Assert.Equal(new[] { 1000, 1001 }, subtitles.Select(s => s.Index));
        Assert.All(subtitles, s => Assert.True(s.IsExternal && s.SupportsExternalStream && s.Codec == "srt"));
        Assert.Equal("eng", subtitles[0].Language);
        Assert.Null(subtitles[0].Title);
        Assert.Null(subtitles[1].Language);
        Assert.Equal("Klingon", subtitles[1].Title);
        Assert.StartsWith($"{Internal}/Currents/subtitles/", subtitles[0].Path, StringComparison.Ordinal);
        Assert.EndsWith(".srt", subtitles[0].Path, StringComparison.Ordinal);
        var token = subtitles[0].Path[(Internal.Length + "/Currents/subtitles/".Length)..^".srt".Length];
        Assert.True(new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryReadSubtitle(token, out var ticket, out var key));
        Assert.Equal(entry.Ticket, ticket);
        Assert.Equal(StreamSubtitles.Key("https://subs.example.com/file/1?key=SUBSECRET"), key);
        Assert.DoesNotContain("SUBSECRET", subtitles[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_facing_subtitle_paths_are_placeholders()
    {
        var source = Create().Build(Entry(WithSubtitles()), Context(redact: true));

        var json = JsonSerializer.Serialize(source);
        Assert.DoesNotContain("subs.example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/Currents/", json, StringComparison.Ordinal);
        Assert.StartsWith("currents://subtitle/", source.MediaStreams.First(s => s.Index == 1000).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_subtitles_can_be_switched_off()
    {
        _settings.Current.EnableSubtitles = false;

        var source = Create().Build(Entry(WithSubtitles()), Context());

        Assert.DoesNotContain(source.MediaStreams, s => s.IsExternal);
    }

    private VersionEntry Probed(long? size, params MediaStream[] streams)
    {
        // The default entry's AIOStreams size (18.4 GB) would stand in for an unknown probe size, so clear it.
        var result = Entry().Stream.Result;
        result.Size = null;
        var entry = Entry(result);
        _probes.Set(entry.Stream.Key, ProbedMedia.From(new MediaSourceInfo { Container = "mkv", Size = size, MediaStreams = [.. streams] }));
        return entry;
    }

    private static MediaStream Track(MediaStreamType type, int index, string? codec) => new() { Type = type, Index = index, Codec = codec };

    [Fact]
    public void Builtin_text_subtitles_are_hidden_over_the_limit()
    {
        var entry = Probed(
            20_000_000_000,
            Track(MediaStreamType.Video, 0, "hevc"),
            Track(MediaStreamType.Audio, 1, "eac3"),
            Track(MediaStreamType.Subtitle, 2, "PGSSUB"),
            Track(MediaStreamType.Subtitle, 3, "subrip"),
            Track(MediaStreamType.Subtitle, 4, "ass"));

        var display = Create().Build(entry, Context(redact: true));
        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 0, 1, 2 }, display.MediaStreams.Select(s => s.Index));
        Assert.Equal(new[] { 0, 1, 2 }, playback.MediaStreams.Select(s => s.Index));
        Assert.True(Create().HidesBuiltInSubtitles(entry));
    }

    [Fact]
    public void Only_trailing_builtin_text_subtitles_leave_the_playback_view()
    {
        // ffmpeg maps embedded streams by position (EncodingHelper.FindIndex): removing subtitle 1 would make audio 2 map as 0:1.
        var entry = Probed(
            20_000_000_000,
            Track(MediaStreamType.Video, 0, "h264"),
            Track(MediaStreamType.Subtitle, 1, "subrip"),
            Track(MediaStreamType.Audio, 2, "aac"),
            Track(MediaStreamType.Subtitle, 3, "subrip"),
            Track(MediaStreamType.Subtitle, 4, "subrip"));

        var display = Create().Build(entry, Context(redact: true));
        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 0, 2 }, display.MediaStreams.Select(s => s.Index));
        Assert.Equal(new[] { 0, 1, 2 }, playback.MediaStreams.Select(s => s.Index));
    }

    [Fact]
    public void Builtin_text_subtitles_before_trailing_cover_art_leave_the_playback_view()
    {
        var entry = Probed(
            20_000_000_000,
            Track(MediaStreamType.Video, 0, "h264"),
            Track(MediaStreamType.Audio, 1, "aac"),
            Track(MediaStreamType.Subtitle, 2, "subrip"),
            Track(MediaStreamType.Subtitle, 3, "subrip"),
            Track(MediaStreamType.EmbeddedImage, 4, "mjpeg"));

        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 0, 1, 4 }, playback.MediaStreams.Select(s => s.Index));
    }

    [Fact]
    public void A_graphical_subtitle_still_stops_the_playback_walk()
    {
        var entry = Probed(
            20_000_000_000,
            Track(MediaStreamType.Video, 0, "h264"),
            Track(MediaStreamType.Audio, 1, "aac"),
            Track(MediaStreamType.Subtitle, 2, "subrip"),
            Track(MediaStreamType.Subtitle, 3, "PGSSUB"),
            Track(MediaStreamType.EmbeddedImage, 4, "mjpeg"));

        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, playback.MediaStreams.Select(s => s.Index));
    }

    [Theory]
    [InlineData(0, 20_000_000_000L)]
    [InlineData(15, 15_000_000_000L)]
    [InlineData(15, 10_000_000_000L)]
    [InlineData(15, null)]
    [InlineData(-3, 20_000_000_000L)]
    public void Zero_means_no_limit(int limitGb, long? size)
    {
        _settings.Current.EmbeddedSubtitleMaxGb = limitGb;
        var entry = Probed(size, Track(MediaStreamType.Video, 0, "h264"), Track(MediaStreamType.Audio, 1, "aac"), Track(MediaStreamType.Subtitle, 2, "subrip"));

        var playback = Create().Build(entry, Context() with { ForPlayback = true });

        Assert.Equal(new[] { 0, 1, 2 }, playback.MediaStreams.Select(s => s.Index));
        Assert.False(Create().HidesBuiltInSubtitles(entry));
    }

    [Fact]
    public void Unprobed_display_hides_builtin_subtitles_of_unknown_codec_but_keeps_stream_subtitles()
    {
        var result = Entry().Stream.Result;
        result.Size = 40_000_000_000;
        result.ParsedFile!.SubtitleTracks = [new MediaTrack { Lang = "English" }, new MediaTrack { Codec = "subrip", Lang = "French" }];
        result.Subtitles = [new StremioSubtitle { Url = "https://subs.example.com/en.srt", Lang = "eng" }];
        var entry = Entry(result);

        var display = Create().Build(entry, Context(redact: true));

        Assert.DoesNotContain(display.MediaStreams, s => s.Type == MediaStreamType.Subtitle && !s.IsExternal);
        Assert.Contains(display.MediaStreams, s => s.Index == TrackIndexes.StreamSubtitles);
    }
}
