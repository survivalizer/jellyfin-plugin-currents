using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Pre-fills Jellyfin track info from AIOStreams' parse of the release name. Codec names are ffmpeg's, which client device profiles compare against.</summary>
public static class MediaStreamMapper
{
    private static readonly Dictionary<string, string> VideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AV1"] = "av1",
        ["HEVC"] = "hevc",
        ["AVC"] = "h264",
        ["VC-1"] = "vc1",
        ["XviD"] = "mpeg4",
        ["DivX"] = "mpeg4",
        ["MPEG-4"] = "mpeg4",
    };

    // First match wins, in this order (a release tagged "Atmos TrueHD" is TrueHD).
    private static readonly (string Tag, string Codec, string? Profile)[] AudioCodecs =
    [
        ("TrueHD", "truehd", null), ("DD+", "eac3", null), ("DD", "ac3", null),
        ("DTS:X", "dts", "DTS-HD MA + DTS:X"), ("DTS-HD MA", "dts", "DTS-HD MA"), ("DTS-HD", "dts", "DTS-HD HRA"),
        ("DTS-ES", "dts", "DTS-ES"), ("DTS", "dts", "DTS"), ("OPUS", "opus", null), ("FLAC", "flac", null), ("AAC", "aac", "LC"),
    ];

    private static readonly Dictionary<string, (int Width, int Height)> Resolutions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2160p"] = (3840, 2160),
        ["1440p"] = (2560, 1440),
        ["1080p"] = (1920, 1080),
        ["720p"] = (1280, 720),
        ["576p"] = (1024, 576),
        ["480p"] = (854, 480),
        ["360p"] = (640, 360),
        ["240p"] = (426, 240),
        ["144p"] = (256, 144),
    };

    private static readonly Dictionary<string, (int Channels, string Layout)> ChannelLayouts = new(StringComparer.Ordinal)
    {
        ["2.0"] = (2, "stereo"),
        ["5.1"] = (6, "5.1"),
        ["6.1"] = (7, "6.1"),
        ["7.1"] = (8, "7.1"),
    };

    // ISO 639-2/B codes, as ffprobe reports them.
    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["English"] = "eng",
        ["Japanese"] = "jpn",
        ["French"] = "fre",
        ["German"] = "ger",
        ["Spanish"] = "spa",
        ["Italian"] = "ita",
        ["Portuguese"] = "por",
        ["Russian"] = "rus",
        ["Korean"] = "kor",
        ["Chinese"] = "chi",
        ["Hindi"] = "hin",
        ["Arabic"] = "ara",
        ["Dutch"] = "dut",
        ["Polish"] = "pol",
        ["Turkish"] = "tur",
        ["Swedish"] = "swe",
        ["Danish"] = "dan",
        ["Norwegian"] = "nor",
        ["Finnish"] = "fin",
        ["Czech"] = "cze",
        ["Hungarian"] = "hun",
        ["Greek"] = "gre",
        ["Hebrew"] = "heb",
        ["Thai"] = "tha",
        ["Vietnamese"] = "vie",
        ["Indonesian"] = "ind",
        ["Ukrainian"] = "ukr",
        ["Tamil"] = "tam",
        ["Telugu"] = "tel",
        ["Malayalam"] = "mal",
    };

    private static readonly HashSet<string> NotLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown", "Multi", "Dual Audio", "Dubbed", "Original",
    };

    private static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mkv", "mp4", "avi", "mov", "m4v", "ts", "webm", "wmv", "flv", "m2ts", "mpg", "mpeg",
    };

    public static PrefilledMedia Prefill(StreamResult result, long? itemRunTimeTicks)
    {
        var parsed = result.ParsedFile;
        var tags = NonBlank(parsed?.VisualTags);
        var runtime = RunTimeTicks(result) ?? (itemRunTimeTicks is > 0 ? itemRunTimeTicks : null);
        var total = TotalBitrate(result, runtime);
        var languages = parsed?.Languages?.Where(l => !string.IsNullOrWhiteSpace(l) && !NotLanguages.Contains(l)).ToList() ?? [];
        var audio = Audio(parsed, languages);
        var video = Video(parsed, tags, total is { } t && audio.BitRate is { } a && t > a ? t - a : null);

        var needsProbe = video.Codec is null || video.Width is null || audio.Codec is null
            || runtime is null || total is null
            || tags.Any(IsDolbyVision) || languages.Count > 1;
        return new PrefilledMedia([video, audio], Container(result), runtime, total, needsProbe);
    }

    public static string Container(StreamResult result)
    {
        var parsed = result.ParsedFile;
        var candidates = new[]
        {
            parsed?.Container,
            parsed?.Extension,
            Path.GetExtension(result.Filename ?? string.Empty),
            Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath) : null,
        };
        foreach (var candidate in candidates)
        {
            var name = candidate?.Trim().TrimStart('.');
            if (!string.IsNullOrEmpty(name))
            {
                return Containers.TryGetValue(name, out var known) ? known : "mkv";
            }
        }

        return "mkv";
    }

    public static long? RunTimeTicks(StreamResult result) =>
        result.Duration is double ms && ms >= 1 && ms < TimeSpan.MaxValue.TotalMilliseconds
            ? (long)(ms * TimeSpan.TicksPerMillisecond)
            : null;

    private static int? TotalBitrate(StreamResult result, long? runtime)
    {
        if (result.Bitrate is double bps && bps >= 1 && bps <= int.MaxValue)
        {
            return (int)bps;
        }

        if (result.Size is > 0 && runtime is > 0)
        {
            var estimate = result.Size.Value * 8d / TimeSpan.FromTicks(runtime.Value).TotalSeconds;
            return estimate is >= 1 and <= int.MaxValue ? (int)estimate : null;
        }

        return null;
    }

    private static bool IsDolbyVision(string tag) =>
        tag.Equals("DV", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("DV Only", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("HDR+DV", StringComparison.OrdinalIgnoreCase);

    // AIOStreams lists can hold null or blank entries; they carry no information.
    private static List<string> NonBlank(List<string>? values) =>
        values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];

    private static bool Has(List<string> tags, string tag) => tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    private static MediaStream Video(ParsedFile? parsed, List<string> tags, int? bitRate)
    {
        var codec = parsed?.Encode is { } encode && VideoCodecs.TryGetValue(encode, out var c) ? c : null;
        var dolbyVision = tags.Any(IsDolbyVision);
        var hlg = Has(tags, "HLG");
        var hdr10Plus = Has(tags, "HDR10+");
        var hdr10 = hdr10Plus || Has(tags, "HDR10") || Has(tags, "HDR") || Has(tags, "HDR Only") || Has(tags, "HDR+DV");
        var tenBit = dolbyVision || hlg || hdr10 || Has(tags, "10bit");

        var video = new MediaStream
        {
            Type = MediaStreamType.Video,
            Index = -1,
            IsDefault = true,
            IsInterlaced = false,
            Codec = codec,
            BitRate = bitRate,
            BitDepth = tenBit ? 10 : 8,
            PixelFormat = tenBit ? "yuv420p10le" : "yuv420p",
            Profile = codec switch
            {
                "hevc" => tenBit ? "Main 10" : "Main",
                "h264" => "High",
                "av1" => "Main",
                _ => null,
            },
        };

        if (parsed?.Resolution is { } resolution && Resolutions.TryGetValue(resolution, out var size))
        {
            video.Width = size.Width;
            video.Height = size.Height;
            video.AspectRatio = (double)size.Width / size.Height >= 1.7 ? "16:9" : "4:3";
        }

        if (dolbyVision && hdr10)
        {
            // Profile 8.1: Dolby Vision over an HDR10 base layer.
            (video.DvProfile, video.DvLevel, video.DvBlSignalCompatibilityId, video.RpuPresentFlag, video.BlPresentFlag) = (8, 6, 1, 1, 1);
            SetPq(video);
        }
        else if (dolbyVision)
        {
            // Profile 5: Dolby Vision only, no HDR10 fallback.
            (video.DvProfile, video.DvLevel, video.DvBlSignalCompatibilityId, video.RpuPresentFlag, video.BlPresentFlag) = (5, 6, 0, 1, 1);
        }
        else if (hlg)
        {
            video.ColorTransfer = "arib-std-b67";
            video.ColorPrimaries = "bt2020";
            video.ColorSpace = "bt2020nc";
        }
        else if (hdr10)
        {
            SetPq(video);
        }
        else
        {
            video.ColorTransfer = "bt709";
        }

        if (hdr10Plus)
        {
            video.Hdr10PlusPresentFlag = true;
        }

        return video;
    }

    private static void SetPq(MediaStream video)
    {
        video.ColorTransfer = "smpte2084";
        video.ColorPrimaries = "bt2020";
        video.ColorSpace = "bt2020nc";
    }

    private static MediaStream Audio(ParsedFile? parsed, List<string> languages)
    {
        var tags = NonBlank(parsed?.AudioTags);
        var atmos = Has(tags, "Atmos");
        var match = AudioCodecs.FirstOrDefault(a => Has(tags, a.Tag));
        string? codec = match.Codec;
        var profile = match.Profile;
        if (codec is null && atmos)
        {
            var disc = parsed?.Quality is { } quality
                && (quality.Contains("REMUX", StringComparison.OrdinalIgnoreCase) || quality.Contains("BluRay", StringComparison.OrdinalIgnoreCase));
            codec = disc ? "truehd" : "eac3";
        }

        if (atmos && codec is "truehd" or "eac3")
        {
            profile = codec == "truehd" ? "Dolby TrueHD + Dolby Atmos" : "Dolby Digital Plus + Dolby Atmos";
        }

        var layout = NonBlank(parsed?.AudioChannels).FirstOrDefault(ChannelLayouts.ContainsKey);
        var (channels, channelLayout) = layout is not null
            ? ChannelLayouts[layout]
            : codec is "aac" or "opus" ? (2, "stereo") : (6, "5.1");

        var audio = new MediaStream
        {
            Type = MediaStreamType.Audio,
            Index = -1,
            IsDefault = true,
            Codec = codec,
            Profile = profile,
            Channels = codec is null ? null : channels,
            ChannelLayout = codec is null ? null : channelLayout,
            SampleRate = codec is null ? null : 48000,
            BitRate = codec is null ? null : EstimatedAudioBitrate(codec, profile, channels),
        };

        if (languages.Count == 1)
        {
            audio.Language = LanguageCodes.TryGetValue(languages[0], out var code) ? code : null;
        }
        else if (languages.Count > 1)
        {
            audio.Title = string.Join(", ", languages);
        }

        return audio;
    }

    // Mirrors Jellyfin's ProbeResultNormalizer.GetEstimatedAudioBitrate.
    private static int EstimatedAudioBitrate(string codec, string? profile, int channels) => codec switch
    {
        "aac" => channels <= 2 ? 192_000 : 320_000,
        "ac3" or "eac3" when profile is null || !profile.Contains("Atmos", StringComparison.Ordinal) => channels <= 2 ? 192_000 : 640_000,
        "eac3" => 768_000,
        "dts" when profile is "DTS-HD MA" or "DTS-HD MA + DTS:X" => channels * 700_000,
        "dts" => channels <= 2 ? 768_000 : 1_509_000,
        "truehd" => channels * 700_000,
        "flac" => channels * 480_000,
        "opus" => channels <= 2 ? 128_000 : 256_000,
        _ => channels <= 2 ? 192_000 : 640_000,
    };
}
