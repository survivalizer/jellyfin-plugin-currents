using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamSubtitlesTests
{
    private static StremioSubtitle Sub(string? url, string? lang, string id = "1") => new() { Id = id, Url = url, Lang = lang };

    [Fact]
    public void Errors_duplicates_and_non_http_urls_are_dropped_and_each_language_is_capped()
    {
        var english = Enumerable.Range(0, 7).Select(i => Sub($"https://subs.example.com/en/{i}", "eng"));
        var others = new[]
        {
            Sub("https://subs.example.com/en/0", "eng"),
            Sub("https://github.com/Viren070/AIOStreams", "[❌] Addon - failed", "error.Addon"),
            Sub("ftp://subs.example.com/x", "fre"),
            Sub(null, "fre"),
            Sub("https://subs.example.com/fr/1", "French"),
        };

        var usable = StreamSubtitles.Usable(english.Concat(others));

        Assert.Equal(6, usable.Count);
        Assert.Equal(5, usable.Count(s => s.Lang == "eng"));
        Assert.Equal("https://subs.example.com/fr/1", usable[^1].Url);
    }

    [Fact]
    public void The_whole_list_is_capped()
    {
        var many = Enumerable.Range(0, 100).Select(i => Sub($"https://subs.example.com/{i}", $"x{i:00}"));

        Assert.Equal(40, StreamSubtitles.Usable(many).Count);
        Assert.Equal(3, StreamSubtitles.Usable(many, perLanguage: 5, total: 3).Count);
    }

    [Fact]
    public void Keys_are_short_stable_and_hide_the_url()
    {
        var key = StreamSubtitles.Key("https://subs.example.com/file?key=SECRET");

        Assert.Matches("^[0-9a-f]{16}$", key);
        Assert.Equal(key, StreamSubtitles.Key("https://subs.example.com/file?key=SECRET"));
        Assert.NotEqual(key, StreamSubtitles.Key("https://subs.example.com/file?key=OTHER"));
    }
}
