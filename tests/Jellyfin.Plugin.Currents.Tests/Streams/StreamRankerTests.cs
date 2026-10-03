using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamRankerTests
{
    [Fact]
    public void Keeps_order_and_drops_unplayable_entries()
    {
        StreamResult[] results =
        [
            new() { Url = "https://a.example.com/1.mkv", Filename = "first" },
            new() { Url = "https://a.example.com/err", Type = "error" },
            new() { Url = null, Filename = "torrent-only" },
            new() { Url = "magnet:?xt=urn:btih:abc", Filename = "magnet" },
            new() { Url = "https://a.example.com/stat", Type = "statistic" },
            new() { Url = "http://b.example.com/2.mkv", Filename = "second" },
        ];

        var ranked = StreamRanker.Rank(results);

        Assert.Equal(new[] { "first", "second" }, ranked.Select(r => r.Filename));
    }

    private static StreamResult S(string name, string? res = null, string[]? tags = null, string[]? langs = null, long? size = null, bool? cached = null, string[]? subs = null) =>
        new()
        {
            Url = $"https://cdn.example.com/{name}",
            Filename = name,
            Size = size,
            Cached = cached,
            ParsedFile = new ParsedFile { Resolution = res, VisualTags = tags?.ToList(), Languages = langs?.ToList(), Subtitles = subs?.ToList() },
        };

    private static string[] Names(IEnumerable<StreamResult> results) => results.Select(r => r.Filename!).ToArray();

    [Fact]
    public void Empty_preferences_keep_aiostreams_order()
    {
        var ranked = StreamRanker.Rank([S("a", "720p"), S("b", "2160p"), S("c", "1080p")], new StreamPreferences());

        Assert.Equal(new[] { "a", "b", "c" }, Names(ranked));
    }

    [Fact]
    public void Streams_needing_request_headers_are_kept()
    {
        var headed = S("h");
        headed.RequestHeaders = new Dictionary<string, string> { ["Referer"] = "https://example.com" };

        Assert.Equal(new[] { "h", "a" }, Names(StreamRanker.Rank([headed, S("a")], new StreamPreferences())));
    }

    [Fact]
    public void Cached_only_drops_uncached_debrid_but_keeps_non_debrid()
    {
        var ranked = StreamRanker.Rank([S("uncached", cached: false), S("cached", cached: true), S("http")], new StreamPreferences { CachedOnly = true });

        Assert.Equal(new[] { "cached", "http" }, Names(ranked));
    }

    [Fact]
    public void Max_size_drops_larger_streams_and_keeps_unknown_sizes()
    {
        var ranked = StreamRanker.Rank([S("big", size: 30_000_000_000), S("small", size: 4_000_000_000), S("unknown")], new StreamPreferences { MaxSizeGb = 20 });

        Assert.Equal(new[] { "small", "unknown" }, Names(ranked));
    }

    [Fact]
    public void Excluded_resolutions_and_visual_tags_are_case_insensitive()
    {
        var prefs = new StreamPreferences { ExcludedResolutions = ["480P"], ExcludedVisualTags = ["3d"] };

        var ranked = StreamRanker.Rank([S("sd", "480p"), S("threed", "1080p", ["3D"]), S("ok", "1080p", ["HDR10"])], prefs);

        Assert.Equal(new[] { "ok" }, Names(ranked));
    }

    [Fact]
    public void Resolution_order_reranks_and_unlisted_go_last_in_original_order()
    {
        var prefs = new StreamPreferences { ResolutionOrder = ["1080p", "2160p"] };

        var ranked = StreamRanker.Rank([S("u1", "720p"), S("k1", "2160p"), S("f1", "1080p"), S("u2"), S("f2", "1080p")], prefs);

        Assert.Equal(new[] { "f1", "f2", "k1", "u1", "u2" }, Names(ranked));
    }

    [Theory]
    [InlineData(HdrPreference.Prefer, new[] { "dv", "hdr", "sdr" })]
    [InlineData(HdrPreference.Avoid, new[] { "sdr", "dv", "hdr" })]
    [InlineData(HdrPreference.Any, new[] { "sdr", "dv", "hdr" })]
    public void Hdr_preference_moves_hdr_streams(HdrPreference hdr, string[] expected)
    {
        var ranked = StreamRanker.Rank([S("sdr", "2160p"), S("dv", "2160p", ["DV"]), S("hdr", "2160p", ["HDR10+"])], new StreamPreferences { Hdr = hdr });

        Assert.Equal(expected, Names(ranked));
    }

    [Fact]
    public void Language_preferences_rank_audio_then_subtitles()
    {
        var prefs = new StreamPreferences { AudioLanguages = ["Japanese", "English"], SubtitleLanguages = ["English"] };

        var ranked = StreamRanker.Rank(
            [S("en", langs: ["English"]), S("none"), S("ja", langs: ["Japanese"]), S("ja-subbed", langs: ["Japanese"], subs: ["English"])],
            prefs);

        Assert.Equal(new[] { "ja-subbed", "ja", "en", "none" }, Names(ranked));
    }

    [Fact]
    public void Resolution_outranks_hdr_and_language()
    {
        var prefs = new StreamPreferences { ResolutionOrder = ["1080p"], Hdr = HdrPreference.Prefer, AudioLanguages = ["English"] };

        var ranked = StreamRanker.Rank([S("uhd-hdr-en", "2160p", ["HDR10"], ["English"]), S("fhd", "1080p")], prefs);

        Assert.Equal(new[] { "fhd", "uhd-hdr-en" }, Names(ranked));
    }

    [Fact]
    public void Normalize_replaces_null_lists_from_untrusted_json()
    {
        var prefs = System.Text.Json.JsonSerializer.Deserialize<StreamPreferences>("""{"resolutionOrder":null,"audioLanguages":null}""", Jellyfin.Plugin.Currents.Common.JsonDefaults.Options)!;

        prefs.Normalize();

        Assert.Empty(prefs.ResolutionOrder);
        Assert.Empty(prefs.AudioLanguages);
        Assert.Empty(StreamRanker.Rank([], prefs));
    }
}
