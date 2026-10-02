using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class MediaStreamMapperTests
{
    private static StreamResult Parsed(ParsedFile parsed, long? size = null, double? durationMs = null, double? bitrate = null, string? filename = null) =>
        new() { ParsedFile = parsed, Size = size, Duration = durationMs, Bitrate = bitrate, Filename = filename, Url = "https://x/stream" };

    [Fact]
    public void Uhd_dolby_vision_truehd_atmos()
    {
        var media = MediaStreamMapper.Prefill(
            Parsed(
                new ParsedFile { Resolution = "2160p", Encode = "HEVC", Quality = "BluRay REMUX", VisualTags = ["DV", "HDR10"], AudioTags = ["Atmos", "TrueHD"], AudioChannels = ["7.1"], Languages = ["English"] },
                size: 58_000_000_000,
                durationMs: 8_880_000),
            itemRunTimeTicks: null);

        var (video, audio) = (media.Streams[0], media.Streams[1]);
        Assert.Equal(MediaStreamType.Video, video.Type);
        Assert.Equal(-1, video.Index);
        Assert.Equal("hevc", video.Codec);
        Assert.Equal("Main 10", video.Profile);
        Assert.Equal((3840, 2160), (video.Width, video.Height));
        Assert.Equal(10, video.BitDepth);
        Assert.Equal(VideoRangeType.DOVIWithHDR10, video.VideoRangeType);
        Assert.Equal(MediaStreamType.Audio, audio.Type);
        Assert.Equal(-1, audio.Index);
        Assert.Equal("truehd", audio.Codec);
        Assert.Equal("Dolby TrueHD + Dolby Atmos", audio.Profile);
        Assert.Equal(8, audio.Channels);
        Assert.Equal(5_600_000, audio.BitRate);
        Assert.Equal("eng", audio.Language);
        Assert.Equal(52_252_252, media.Bitrate);
        Assert.Equal(52_252_252 - 5_600_000, video.BitRate);
        Assert.Equal(TimeSpan.FromMilliseconds(8_880_000).Ticks, media.RunTimeTicks);
        Assert.True(media.NeedsProbe); // Dolby Vision
    }

    [Fact]
    public void Hd_sdr_h264_with_dd_plus_needs_no_probe()
    {
        var media = MediaStreamMapper.Prefill(
            Parsed(new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["DD+"], AudioChannels = ["5.1"], Languages = ["English"] }, size: 4_000_000_000, durationMs: 7_200_000),
            itemRunTimeTicks: null);

        Assert.Equal("h264", media.Streams[0].Codec);
        Assert.Equal("High", media.Streams[0].Profile);
        Assert.Equal(8, media.Streams[0].BitDepth);
        Assert.Equal(VideoRangeType.SDR, media.Streams[0].VideoRangeType);
        Assert.Equal("eac3", media.Streams[1].Codec);
        Assert.Equal(6, media.Streams[1].Channels);
        Assert.Equal(640_000, media.Streams[1].BitRate);
        Assert.Equal(4_444_444, media.Bitrate);
        Assert.Equal(4_444_444 - 640_000, media.Streams[0].BitRate);
        Assert.False(media.NeedsProbe);
    }

    [Theory]
    [InlineData(new[] { "HDR10" }, VideoRangeType.HDR10)]
    [InlineData(new[] { "HDR" }, VideoRangeType.HDR10)]
    [InlineData(new[] { "HDR10+" }, VideoRangeType.HDR10Plus)]
    [InlineData(new[] { "HLG" }, VideoRangeType.HLG)]
    [InlineData(new[] { "DV Only" }, VideoRangeType.DOVI)]
    [InlineData(new[] { "DV" }, VideoRangeType.DOVI)]
    [InlineData(new[] { "HDR+DV" }, VideoRangeType.DOVIWithHDR10)]
    [InlineData(new[] { "DV", "HDR10+" }, VideoRangeType.DOVIWithHDR10Plus)]
    [InlineData(new[] { "10bit" }, VideoRangeType.SDR)]
    public void Visual_tags_map_to_jellyfin_video_range(string[] tags, VideoRangeType expected)
    {
        var video = MediaStreamMapper.Prefill(Parsed(new ParsedFile { Resolution = "2160p", Encode = "HEVC", VisualTags = tags.ToList() }), null).Streams[0];

        Assert.Equal(expected, video.VideoRangeType);
    }

    [Fact]
    public void Item_runtime_fills_in_when_aiostreams_has_no_duration()
    {
        var media = MediaStreamMapper.Prefill(
            Parsed(new ParsedFile { Resolution = "1080p", Encode = "HEVC", AudioTags = ["AAC"], AudioChannels = ["2.0"] }, size: 1_800_000_000),
            itemRunTimeTicks: TimeSpan.FromMinutes(60).Ticks);

        Assert.Equal(TimeSpan.FromMinutes(60).Ticks, media.RunTimeTicks);
        Assert.Equal(4_000_000, media.Bitrate);
        Assert.Equal(192_000, media.Streams[1].BitRate);
        Assert.False(media.NeedsProbe);
    }

    [Fact]
    public void Aiostreams_bitrate_wins_over_size_estimate()
    {
        var media = MediaStreamMapper.Prefill(
            Parsed(new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"] }, size: 1_000_000, durationMs: 1_000, bitrate: 8_000_000.7),
            null);

        Assert.Equal(8_000_000, media.Bitrate);
    }

    [Fact]
    public void Unknown_parse_still_yields_video_and_audio_and_asks_for_a_probe()
    {
        var media = MediaStreamMapper.Prefill(new StreamResult { Url = "https://x/a" }, null);

        Assert.Equal(new[] { MediaStreamType.Video, MediaStreamType.Audio }, media.Streams.Select(s => s.Type));
        Assert.All(media.Streams, s => Assert.Equal(-1, s.Index));
        Assert.Null(media.RunTimeTicks);
        Assert.True(media.NeedsProbe);
    }

    [Fact]
    public void Several_languages_ask_for_a_probe()
    {
        var media = MediaStreamMapper.Prefill(
            Parsed(new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"], Languages = ["English", "Japanese", "Multi"] }, size: 1_000_000_000, durationMs: 3_600_000),
            null);

        Assert.Null(media.Streams[1].Language);
        Assert.Equal("English, Japanese", media.Streams[1].Title);
        Assert.True(media.NeedsProbe);
    }

    [Theory]
    [InlineData("BluRay REMUX", "truehd")]
    [InlineData("WEB-DL", "eac3")]
    public void Atmos_alone_is_truehd_on_discs_and_eac3_on_web(string quality, string codec)
    {
        var audio = MediaStreamMapper.Prefill(Parsed(new ParsedFile { Quality = quality, AudioTags = ["Atmos"] }), null).Streams[1];

        Assert.Equal(codec, audio.Codec);
        Assert.Contains("Dolby Atmos", audio.Profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_call_returns_new_stream_objects()
    {
        var result = Parsed(new ParsedFile { Resolution = "1080p", Encode = "AVC" });

        var first = MediaStreamMapper.Prefill(result, null);
        var second = MediaStreamMapper.Prefill(result, null);

        Assert.NotSame(first.Streams[0], second.Streams[0]);
        Assert.NotSame(first.Streams[1], second.Streams[1]);
    }

    [Theory]
    [InlineData("mp4", null, null, "mp4")]
    [InlineData(null, ".MKV", null, "mkv")]
    [InlineData(null, null, "Movie.2020.m2ts", "m2ts")]
    [InlineData(null, null, "Movie.2020.iso", "mkv")]
    [InlineData(null, null, null, "mkv")]
    public void Container_comes_from_parse_then_file_name(string? container, string? extension, string? filename, string expected) =>
        Assert.Equal(expected, MediaStreamMapper.Container(Parsed(new ParsedFile { Container = container, Extension = extension }, filename: filename)));

    [Fact]
    public void Runtime_converts_milliseconds_and_ignores_nonsense()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(8_473_120).Ticks, MediaStreamMapper.RunTimeTicks(new StreamResult { Duration = 8_473_120 }));
        Assert.Null(MediaStreamMapper.RunTimeTicks(new StreamResult { Duration = 0 }));
        Assert.Null(MediaStreamMapper.RunTimeTicks(new StreamResult { Duration = double.NaN }));
        Assert.Null(MediaStreamMapper.RunTimeTicks(new StreamResult()));
    }
}
