using System.Globalization;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Builds a version's track views from the best source available: a probe, then RemuxDB, then AIOStreams' track lists, then the release name.</summary>
public static class TrackComposer
{
    // Cover art is stored as a video stream; it is not the film.
    private static readonly HashSet<string> CoverArt = new(StringComparer.OrdinalIgnoreCase) { "mjpeg", "png", "bmp", "gif", "webp" };

    // The names Jellyfin's own probe normalizer uses (ProbeResultNormalizer.NormalizeSubtitleCodec).
    private static readonly Dictionary<string, string> SubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dvb_subtitle"] = "DVBSUB",
        ["dvb_teletext"] = "DVBTXT",
        ["dvd_subtitle"] = "DVDSUB",
        ["hdmv_pgs_subtitle"] = "PGSSUB",
    };

    public static VersionTracks Compose(StreamResult result, long? itemRunTimeTicks, ProbedMedia? probed, RemuxDbVersion? remux)
    {
        var prefill = MediaStreamMapper.Prefill(result, itemRunTimeTicks);
        if (probed is not null)
        {
            return new VersionTracks(
                probed.Streams(),
                probed.Streams(),
                probed.Container ?? prefill.Container,
                probed.RunTimeTicks ?? prefill.RunTimeTicks,
                probed.Bitrate ?? prefill.Bitrate,
                probed.Size ?? result.Size,
                TrackOrigin.Probe,
                NeedsProbe: false);
        }

        if (remux is not null && FromRemuxDb(remux) is { Count: > 0 } known)
        {
            var bitrate = Bitrate(remux.Bitrate) ?? prefill.Bitrate;
            EstimateVideoBitrate(known, bitrate);
            return Unprobed(known, prefill, RemuxContainer(remux.Container) ?? prefill.Container, Ticks(remux.Duration) ?? prefill.RunTimeTicks, bitrate, remux.Size ?? result.Size, TrackOrigin.RemuxDb);
        }

        if (FromAioStreams(result.ParsedFile, prefill) is { } listed)
        {
            return Unprobed(listed, prefill, prefill.Container, prefill.RunTimeTicks, prefill.Bitrate, result.Size, TrackOrigin.AioStreams);
        }

        return Unprobed(FromReleaseName(result.ParsedFile, prefill), prefill, prefill.Container, prefill.RunTimeTicks, prefill.Bitrate, result.Size, TrackOrigin.ReleaseName);
    }

    private static VersionTracks Unprobed(List<MediaStream> tracks, PrefilledMedia prefill, string container, long? runtime, int? bitrate, long? size, TrackOrigin origin)
    {
        var display = tracks.Take(TrackIndexes.StreamSubtitles - TrackIndexes.Synthetic).ToList();
        for (var i = 0; i < display.Count; i++)
        {
            display[i].Index = TrackIndexes.Synthetic + i;
        }

        // ffmpeg sees one video and one audio stub with Index -1, so it picks the file's default tracks itself (EncodingHelper.GetMapArgs).
        var video = display.Find(s => s.Type == MediaStreamType.Video) is { } v ? Stub(v) : prefill.Streams[0];
        var audioTracks = display.Where(s => s.Type == MediaStreamType.Audio).ToList();
        var audio = (audioTracks.Find(s => s.IsDefault) ?? audioTracks.FirstOrDefault()) is { } a ? Stub(a) : prefill.Streams[1];
        var subtitles = display.Count(s => s.Type == MediaStreamType.Subtitle);
        var needsProbe = audioTracks.Count > 1
            || subtitles > 0
            || (origin == TrackOrigin.ReleaseName
                ? prefill.NeedsProbe
                : video.Codec is null || audio.Codec is null || runtime is null || bitrate is null
                    || (origin == TrackOrigin.AioStreams && prefill.NeedsProbe));
        return new VersionTracks(display, [video, audio], container, runtime, bitrate, size, origin, needsProbe);
    }

    private static MediaStream Stub(MediaStream stream)
    {
        var stub = MediaStreamCopy.Of(stream);
        stub.Index = -1;
        return stub;
    }

    private static List<MediaStream> FromRemuxDb(RemuxDbVersion version)
    {
        var tracks = (version.Tracks ?? []).Where(t => t is not null && t.IsExternal != true).OrderBy(t => t.Idx ?? int.MaxValue).ToList();
        var streams = new List<MediaStream>();
        if (tracks.Find(t => t.Kind == "video" && !CoverArt.Contains(t.Codec ?? string.Empty)) is { } video)
        {
            streams.Add(Video(video));
        }

        streams.AddRange(tracks.Where(t => t.Kind == "audio").Select(Audio));
        streams.AddRange(tracks.Where(t => t.Kind == "subtitle").Select(t => Subtitle(t.Codec, t.Language, t.IsForced, t.IsHearingImpaired, t.IsDefault)));
        return streams;
    }

    // Crowd-sourced titles are dropped (release-group branding), as Remux does.
    private static MediaStream Video(RemuxDbTrack track)
    {
        var tenBit = track.BitDepth >= 10 || (track.PixelFormat?.Contains("10", StringComparison.Ordinal) ?? false);
        var video = new MediaStream
        {
            Type = MediaStreamType.Video,
            IsDefault = true,
            IsInterlaced = false,
            Codec = track.Codec,
            Profile = track.Profile,
            Width = track.Width,
            Height = track.Height,
            BitDepth = track.BitDepth ?? (tenBit ? 10 : 8),
            PixelFormat = track.PixelFormat,
            ColorTransfer = track.ColorTransfer,
            ColorPrimaries = track.ColorPrimaries,
            ColorSpace = track.ColorSpace?.Replace("_", string.Empty, StringComparison.Ordinal),
            BitRate = Bitrate(track.BitRate),
            Hdr10PlusPresentFlag = track.Hdr10PlusPresent == true ? true : null,
        };

        if (track.DvProfile is { } profile)
        {
            (video.DvProfile, video.RpuPresentFlag, video.BlPresentFlag) = (profile, 1, 1);
            video.DvBlSignalCompatibilityId = profile switch
            {
                5 => 0,
                7 => 6,
                8 => track.ColorTransfer == "arib-std-b67" ? 4 : 1,
                _ => null,
            };
        }

        if (track.Width is > 0 && track.Height is > 0)
        {
            video.AspectRatio = (double)track.Width.Value / track.Height.Value >= 1.7 ? "16:9" : "4:3";
        }

        return video;
    }

    private static MediaStream Audio(RemuxDbTrack track) => new()
    {
        Type = MediaStreamType.Audio,
        Codec = track.Codec,
        Profile = track.Profile,
        Language = LanguageCodes.ToIso6392(track.Language),
        Channels = track.Channels,
        ChannelLayout = Layout(track.ChannelLayout),
        SampleRate = track.SampleRate,
        BitRate = Bitrate(track.BitRate),
        IsDefault = track.IsDefault == true,
        IsForced = track.IsForced == true,
    };

    private static MediaStream Subtitle(string? codec, string? language, bool? forced, bool? hearingImpaired, bool? isDefault) => new()
    {
        Type = MediaStreamType.Subtitle,
        Codec = codec is null ? null : SubtitleCodecs.GetValueOrDefault(codec, codec),
        Language = LanguageCodes.ToIso6392(language),
        IsForced = forced == true,
        IsHearingImpaired = hearingImpaired == true,
        IsDefault = isDefault == true,
    };

    private static List<MediaStream>? FromAioStreams(ParsedFile? parsed, PrefilledMedia prefill)
    {
        var audio = parsed?.AudioTracks?.Where(t => t is not null).ToList() ?? [];
        var subtitles = parsed?.SubtitleTracks?.Where(t => t is not null).ToList() ?? [];
        if (audio.Count == 0 && subtitles.Count == 0)
        {
            return null;
        }

        var streams = new List<MediaStream> { MediaStreamCopy.Of(prefill.Streams[0]) };
        streams.AddRange(audio.Count == 0 ? ReleaseAudio(parsed, prefill) : audio.Select((t, i) => Audio(t, prefill.Streams[1], i == 0)));
        streams.AddRange(subtitles.Select(t => Subtitle(t.Codec, t.Lang, t.Forced, t.HearingImpaired, t.Default)));
        return streams;
    }

    private static MediaStream Audio(MediaTrack track, MediaStream guess, bool first)
    {
        var (channels, layout) = Channels(track.Channels);
        var codec = string.IsNullOrWhiteSpace(track.Codec) ? null : track.Codec.Trim();
        return new MediaStream
        {
            Type = MediaStreamType.Audio,
            Codec = codec ?? (first ? guess.Codec : null),
            Language = LanguageCodes.ToIso6392(track.Lang),
            Channels = channels ?? (first ? guess.Channels : null),
            ChannelLayout = layout ?? (first ? guess.ChannelLayout : null),
            IsDefault = track.Default ?? first,
            IsForced = track.Forced == true,
            IsHearingImpaired = track.HearingImpaired == true,
            Title = track.Commentary == true ? "Commentary" : null,
        };
    }

    private static List<MediaStream> FromReleaseName(ParsedFile? parsed, PrefilledMedia prefill)
    {
        var streams = new List<MediaStream> { MediaStreamCopy.Of(prefill.Streams[0]) };
        streams.AddRange(ReleaseAudio(parsed, prefill));
        streams.AddRange(MediaStreamMapper.SubtitleLanguages(parsed).Select(code => Subtitle(null, code, false, false, false)));
        return streams;
    }

    // One audio track per release-name language; the order is a guess, so mapping to the real track goes by language.
    private static IEnumerable<MediaStream> ReleaseAudio(ParsedFile? parsed, PrefilledMedia prefill)
    {
        var languages = MediaStreamMapper.AudioLanguages(parsed);
        if (languages.Count <= 1)
        {
            return [MediaStreamCopy.Of(prefill.Streams[1])];
        }

        return languages.Select((language, i) =>
        {
            var audio = MediaStreamCopy.Of(prefill.Streams[1]);
            (audio.Language, audio.Title, audio.IsDefault) = (language, null, i == 0);
            return audio;
        });
    }

    private static void EstimateVideoBitrate(List<MediaStream> tracks, int? total)
    {
        if (tracks.Find(s => s.Type == MediaStreamType.Video) is { BitRate: null } video && total is { } bitrate)
        {
            var audio = tracks.Where(s => s.Type == MediaStreamType.Audio).Sum(s => (long)(s.BitRate ?? 0));
            video.BitRate = bitrate > audio ? (int)(bitrate - audio) : null;
        }
    }

    // "5.1" → 6, "7.1" → 8, "2.0" → 2, or a plain count.
    private static (int? Channels, string? Layout) Channels(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        var parts = text.Split('.');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var main)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lfe))
        {
            return (main + lfe, text switch { "1.0" => "mono", "2.0" => "stereo", _ => text });
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0
            ? (count, count switch { 1 => "mono", 2 => "stereo", 6 => "5.1", 8 => "7.1", _ => null })
            : (null, null);
    }

    // ffprobe's "5.1(side)" is Jellyfin's "5.1".
    private static string? Layout(string? value)
    {
        var layout = value?.Split('(')[0].Trim();
        return string.IsNullOrEmpty(layout) ? null : layout;
    }

    private static string? RemuxContainer(string? format) => format?.Split(',')[0].Trim() switch
    {
        "matroska" => "mkv",
        "mov" => "mp4",
        "mpegts" => "ts",
        "avi" => "avi",
        _ => null,
    };

    private static long? Ticks(double? seconds) =>
        seconds is > 0 and < 1_000_000 ? (long)(seconds.Value * TimeSpan.TicksPerSecond) : null;

    private static int? Bitrate(long? value) => value is > 0 and <= int.MaxValue ? (int)value.Value : null;
}
