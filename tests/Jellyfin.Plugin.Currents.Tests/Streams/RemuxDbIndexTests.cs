using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class RemuxDbIndexTests
{
    private const string Hash = "f1c8b4ba4ae611494ffb09958241165aaf1f07db";

    private static RemuxDbVersion Version(string? file, int? idx, long size = 2_000_000_000, string hash = Hash) => new()
    {
        Size = size,
        Sources = [new RemuxDbSource { Kind = "torrent", TorrentInfoHash = hash, TorrentFileIdx = idx, Filename = file }],
    };

    private static StreamResult Stream(string? file, int? idx, long? size = 2_000_000_000, string? hash = Hash) =>
        new() { InfoHash = hash, FileIdx = idx, Filename = file, Size = size };

    [Fact]
    public void Same_hash_and_file_index_matches()
    {
        var one = Version("Show.S01E01.mkv", 0);
        var two = Version("Show.S01E02.mkv", 1);

        Assert.Same(two, RemuxDbIndex.Create([one, two]).Match(Stream(null, 1)));
    }

    [Fact]
    public void Same_hash_and_file_name_matches_when_remuxdb_has_no_index()
    {
        var episode = Version("Show.S01.1080p/Show.S01E02.1080p.mkv", null);

        Assert.Same(episode, RemuxDbIndex.Create([Version("Show.S01.1080p/Show.S01E01.1080p.mkv", null), episode]).Match(Stream("show.s01e02.1080p.mkv", null)));
    }

    [Fact]
    public void Same_torrent_other_file_is_not_matched()
    {
        var index = RemuxDbIndex.Create([Version("Show.S01E01.mkv", 0, size: 2_000_000_000)]);

        Assert.Null(index.Match(Stream("Show.S01E02.mkv", 1, size: 2_005_000_000)));
        Assert.Null(index.Match(Stream(null, 1, size: 2_000_000_000)));
        Assert.Null(index.Match(Stream("Show.S01E02.mkv", null, size: 2_000_000_000)));
    }

    [Fact]
    public void Single_candidate_without_file_info_matches_within_one_percent_of_size()
    {
        var only = Version(null, null, size: 2_000_000_000);
        var index = RemuxDbIndex.Create([only]);

        Assert.Same(only, index.Match(Stream(null, null, size: 2_019_000_000)));
        Assert.Null(index.Match(Stream(null, null, size: 2_100_000_000)));
        Assert.Null(index.Match(Stream(null, null, size: null)));
    }

    [Fact]
    public void Ambiguous_candidates_are_not_matched()
    {
        Assert.Null(RemuxDbIndex.Create([Version("A.mkv", 0), Version("A.mkv", 0)]).Match(Stream("A.mkv", 0)));
    }

    [Fact]
    public void Hash_case_is_ignored_and_no_hash_never_matches()
    {
        var index = RemuxDbIndex.Create([Version("A.mkv", 0, hash: Hash.ToUpperInvariant())]);

        Assert.NotNull(index.Match(Stream("A.mkv", 0)));
        Assert.Null(index.Match(Stream("A.mkv", 0, hash: null)));
        Assert.Null(RemuxDbIndex.Empty.Match(Stream("A.mkv", 0)));
    }
}
