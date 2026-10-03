using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public sealed class ProbeCacheTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private string Folder => Path.Combine(_settings.DataFolderPath, "probes");

    private static MediaSourceInfo Probed() => new()
    {
        Container = "mkv",
        RunTimeTicks = 123,
        Bitrate = 9_000_000,
        Size = 42,
        MediaStreams =
        [
            new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "hevc", ColorTransfer = "smpte2084", ColorPrimaries = "bt2020", ColorSpace = "bt2020nc" },
            new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "eac3", Channels = 6, Language = "eng" },
            new MediaStream { Type = MediaStreamType.Subtitle, Index = 2, Codec = "subrip", Language = "eng" },
        ],
    };

    [Fact]
    public void Round_trips_streams_as_new_objects()
    {
        var media = ProbedMedia.From(Probed());

        var first = media.Streams();
        var second = media.Streams();

        Assert.Equal(3, first.Count);
        Assert.Equal("hevc", first[0].Codec);
        Assert.Equal(0, first[0].Index);
        Assert.Equal(VideoRangeType.HDR10, first[0].VideoRangeType);
        Assert.Equal(6, first[1].Channels);
        Assert.NotSame(first[0], second[0]);
        Assert.Equal(("mkv", 123L, 9_000_000, 42L), (media.Container, media.RunTimeTicks!.Value, media.Bitrate!.Value, media.Size!.Value));
    }

    [Fact]
    public void Entries_survive_a_restart()
    {
        new ProbeCache(_settings, _time).Set(Key, ProbedMedia.From(Probed()));

        Assert.True(new ProbeCache(_settings, _time).TryGet(Key, out var media));
        Assert.Equal("eac3", media.Streams()[1].Codec);
        Assert.True(File.Exists(Path.Combine(Folder, Key + ".json")));
        Assert.Empty(Directory.GetFiles(Folder, "*.tmp"));
    }

    [Fact]
    public void Entries_expire_after_thirty_days_in_memory_and_on_disk()
    {
        var cache = new ProbeCache(_settings, _time);
        cache.Set(Key, ProbedMedia.From(Probed()));

        _time.Advance(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));

        Assert.False(cache.TryGet(Key, out _));
        Assert.False(new ProbeCache(_settings, _time).TryGet(Key, out _));
        Assert.False(File.Exists(Path.Combine(Folder, Key + ".json")));
    }

    [Fact]
    public void Corrupt_files_are_misses()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, Key + ".json"), "{ not json");

        Assert.False(new ProbeCache(_settings, _time).TryGet(Key, out _));
    }

    [Fact]
    public void Keys_that_are_not_stream_keys_stay_in_memory()
    {
        var cache = new ProbeCache(_settings, _time);

        cache.Set("../escape", ProbedMedia.From(Probed()));

        Assert.True(cache.TryGet("../escape", out _));
        Assert.False(Directory.Exists(Folder) && Directory.EnumerateFileSystemEntries(Folder).Any());
        Assert.False(File.Exists(Path.Combine(_settings.DataFolderPath, "escape.json")));
    }

    [Fact]
    public void Disk_misses_are_remembered_for_ten_minutes()
    {
        var cache = new ProbeCache(_settings, _time);
        Assert.False(cache.TryGet(Key, out _));

        new ProbeCache(_settings, _time).Set(Key, ProbedMedia.From(Probed()));

        Assert.False(cache.TryGet(Key, out _));
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(cache.TryGet(Key, out _));
    }

    [Fact]
    public void Oldest_files_are_pruned_beyond_the_cap()
    {
        var first = new ProbeCache(_settings, _time, maxFiles: 3);
        var keys = new[] { "a", "b", "c" }.Select(k => new string(k[0], 32)).ToArray();
        foreach (var key in keys)
        {
            first.Set(key, ProbedMedia.From(Probed()));
        }

        for (var i = 0; i < keys.Length; i++)
        {
            File.SetLastWriteTimeUtc(Path.Combine(Folder, keys[i] + ".json"), new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc));
        }

        new ProbeCache(_settings, _time, maxFiles: 3).Set(new string('d', 32), ProbedMedia.From(Probed()));

        var left = Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToArray();
        Assert.Equal(new[] { keys[1], keys[2], new string('d', 32) }, left);
    }
}
