using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamIdentityTests
{
    private static StreamResult Torrent(string hash, int idx, string service = "realdebrid", string url = "https://aio.example.com/play/1") =>
        new() { InfoHash = hash, FileIdx = idx, Service = service, Addon = "Torrentio", Url = url, Filename = "Movie.mkv" };

    [Fact]
    public void Keys_are_stable_across_searches_even_when_playback_urls_change()
    {
        var first = StreamIdentity.Keys([Torrent("ABCDEF", 0, url: "https://aio.example.com/play/aaa")]);
        var second = StreamIdentity.Keys([Torrent("abcdef", 0, url: "https://aio.example.com/play/bbb")]);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{32}$", first[0]);
    }

    [Fact]
    public void Same_file_on_two_services_gets_two_keys()
    {
        var keys = StreamIdentity.Keys([Torrent("abc", 0, "realdebrid"), Torrent("abc", 0, "torbox")]);

        Assert.NotEqual(keys[0], keys[1]);
    }

    [Fact]
    public void Identical_looking_results_are_numbered_so_keys_stay_unique()
    {
        var keys = StreamIdentity.Keys([Torrent("abc", 0), Torrent("abc", 0), Torrent("abc", 0)]);

        Assert.Equal(3, keys.Distinct().Count());
        Assert.Equal(keys, StreamIdentity.Keys([Torrent("abc", 0), Torrent("abc", 0), Torrent("abc", 0)]));
    }

    [Fact]
    public void Falls_back_to_filename_and_size_then_url()
    {
        var byFile = new StreamResult { Filename = "A.mkv", Size = 10, Url = "https://x/1" };
        var byFileOtherUrl = new StreamResult { Filename = "A.mkv", Size = 10, Url = "https://x/2" };
        var byUrl = new StreamResult { Url = "https://x/3" };

        Assert.Equal(StreamIdentity.Keys([byFile])[0], StreamIdentity.Keys([byFileOtherUrl])[0]);
        Assert.NotEqual(StreamIdentity.Keys([byUrl])[0], StreamIdentity.Keys([new StreamResult { Url = "https://x/4" }])[0]);
    }

    [Fact]
    public void Version_ids_are_lower_case_guids_that_differ_per_item_user_and_key()
    {
        var item = Guid.Parse("9bb016e527c151f6e01595cd9d762c5a");
        var alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var id = StreamIdentity.VersionId(item, alice, "k1");

        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, StreamIdentity.VersionId(item, alice, "k1"));
        Assert.NotEqual(id, StreamIdentity.VersionId(item, alice, "k2"));
        Assert.NotEqual(id, StreamIdentity.VersionId(item, bob, "k1"));
        Assert.NotEqual(id, StreamIdentity.VersionId(Guid.NewGuid(), alice, "k1"));
        Assert.NotEqual(item.ToString("N"), id);
    }
}
