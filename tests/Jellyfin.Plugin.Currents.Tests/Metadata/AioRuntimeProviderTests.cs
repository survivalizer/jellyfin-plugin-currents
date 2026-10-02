using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Metadata;

public sealed class AioRuntimeProviderTests : IDisposable
{
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);

    public AioRuntimeProviderTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _client.Metas["movie/tt1"] = new StremioMeta { Id = "tt1", Runtime = "2h 22min" };
        _client.Metas["series/tt2"] = new StremioMeta { Id = "tt2", Runtime = "45 min" };
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private string Strm(string relative, string type, string id)
    {
        var path = Path.Combine(_settings.Current.LibraryRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", type, id));
        return path;
    }

    private AioRuntimeProvider Create() => new(
        new MetaCache(_client, _settings, _time, NullLogger<MetaCache>.Instance),
        new CurrentsItemLocator(_settings, _time));

    [Fact]
    public async Task Movie_gets_its_aiometadata_runtime()
    {
        var movie = new Movie { Path = Strm("Movies/M/M.strm", "movie", "tt1") };

        var result = await Create().FetchAsync(movie, new MetadataRefreshOptions(new DirectoryService(null!)), CancellationToken.None);

        Assert.Equal(ItemUpdateType.MetadataImport, result);
        Assert.Equal(TimeSpan.FromMinutes(142).Ticks, movie.RunTimeTicks);
    }

    [Fact]
    public async Task Episode_gets_the_series_runtime()
    {
        var episode = new Episode { Path = Strm("Shows/S/Season 01/S S01E02.strm", "series", "tt2:1:2") };

        var result = await Create().FetchAsync(episode, new MetadataRefreshOptions(new DirectoryService(null!)), CancellationToken.None);

        Assert.Equal(ItemUpdateType.MetadataImport, result);
        Assert.Equal(TimeSpan.FromMinutes(45).Ticks, episode.RunTimeTicks);
        Assert.Contains("series/tt2", _client.MetaRequests);
    }

    [Fact]
    public async Task Existing_locked_or_foreign_items_are_left_alone()
    {
        var timed = new Movie { Path = Strm("Movies/A/A.strm", "movie", "tt1"), RunTimeTicks = 5 };
        var locked = new Movie { Path = Strm("Movies/B/B.strm", "movie", "tt1"), LockedFields = [MetadataField.Runtime] };
        var foreign = new Movie { Path = Path.Combine(_settings.DataFolderPath, "other.mkv") };
        var options = new MetadataRefreshOptions(new DirectoryService(null!));

        Assert.Equal(ItemUpdateType.None, await Create().FetchAsync(timed, options, CancellationToken.None));
        Assert.Equal(ItemUpdateType.None, await Create().FetchAsync(locked, options, CancellationToken.None));
        Assert.Equal(ItemUpdateType.None, await Create().FetchAsync(foreign, options, CancellationToken.None));
        Assert.Equal(5, timed.RunTimeTicks);
        Assert.Null(locked.RunTimeTicks);
    }

    [Fact]
    public async Task Missing_meta_or_runtime_changes_nothing()
    {
        _client.Metas["movie/tt1"] = new StremioMeta { Id = "tt1" };
        var movie = new Movie { Path = Strm("Movies/M/M.strm", "movie", "tt1") };

        var result = await Create().FetchAsync(movie, new MetadataRefreshOptions(new DirectoryService(null!)), CancellationToken.None);

        Assert.Equal(ItemUpdateType.None, result);
        Assert.Null(movie.RunTimeTicks);
    }
}
