using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class PlaybackInfoFilterTests : IDisposable
{
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly (IMediaSourceManager Instance, InterfaceFake Fake) _media = InterfaceFake.Create<IMediaSourceManager>();
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly Movie _movie;

    public PlaybackInfoFilterTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M", "M.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm, RunTimeTicks = TimeSpan.FromHours(2).Ticks };
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => (Guid)args[0]! == _movie.Id ? _movie : null);

        _client.Outcome = new SearchOutcome([Described("a"), Described("b"), Described("c")], []);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(new StreamService(_client, _settings, _time, NullLogger<StreamService>.Instance), new StreamProfileResolver(users, _settings), _registry, _settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static StreamResult Described(string name) => new()
    {
        Url = $"https://aio.example.com/play/{name}",
        Filename = $"{name}.mkv",
        Size = 4_000_000_000,
        Duration = 7_200_000,
        ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"] },
    };

    private PlaybackInfoFilter Create(Guid user)
    {
        var probes = new ProbeCache(_settings, _time);
        var prober = new VersionProber(_media.Instance, _library.Instance, probes, new VersionSourceBuilder(_settings, _time, probes), new FixedUrl("http://127.0.0.1:8096"), _time, NullLogger<VersionProber>.Instance);
        var request = RequestContextTests.Create(RequestContextTests.Http(user, action: ("MediaInfo", "GetPostedPlaybackInfo")));
        return new PlaybackInfoFilter(_library.Instance, new CurrentsItemLocator(_settings, _time), _catalog, _registry, prober, request, _settings);
    }

    private static Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext PlaybackInfo(Guid itemId, string? mediaSourceId, FakePlaybackInfoDto? dto = null) =>
        SyntheticVersionIdFilterTests.Context("POST", "MediaInfo", "GetPostedPlaybackInfo", new()
        {
            ["itemId"] = itemId,
            ["mediaSourceId"] = mediaSourceId,
            ["playbackInfoDto"] = dto,
        });

    private async Task<IReadOnlyList<VersionEntry>> AliceVersions() =>
        (await _catalog.GetAsync(_movie.Id, new CurrentsTitle("movie", "tt1"), Alice, TimeSpan.FromSeconds(10), CancellationToken.None)).Versions;

    [Fact]
    public async Task Exact_version_id_is_kept()
    {
        var versions = await AliceVersions();
        var context = PlaybackInfo(_movie.Id, versions[1].VersionId);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[1].VersionId, context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Item_id_or_unknown_id_becomes_the_top_version_in_query_and_body()
    {
        var versions = await AliceVersions();
        var dto = new FakePlaybackInfoDto { MediaSourceId = _movie.Id.ToString("N") };
        var context = PlaybackInfo(_movie.Id, null, dto);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[0].VersionId, context.ActionArguments["mediaSourceId"]);
        Assert.Equal(versions[0].VersionId, dto.MediaSourceId);
    }

    [Fact]
    public async Task Stale_version_id_keeps_its_stream_when_still_offered()
    {
        var versions = await AliceVersions();
        var stale = new VersionEntry(Guid.NewGuid().ToString("N"), _movie.Id, Alice, versions[2].Title, versions[2].Stream);
        _registry.Register(Guid.NewGuid(), Alice, [stale]);
        var context = PlaybackInfo(_movie.Id, stale.VersionId);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Equal(versions[2].VersionId, context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task No_requested_version_is_left_for_jellyfin_to_choose()
    {
        var context = PlaybackInfo(_movie.Id, null);

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Null(context.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Unconfigured_user_and_other_items_are_untouched()
    {
        var bob = PlaybackInfo(_movie.Id, "x");
        var other = PlaybackInfo(Guid.NewGuid(), "y");

        await SyntheticVersionIdFilterTests.Run(Create(Bob), bob);
        await SyntheticVersionIdFilterTests.Run(Create(Alice), other);

        Assert.Equal("x", bob.ActionArguments["mediaSourceId"]);
        Assert.Equal("y", other.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task Other_actions_are_ignored()
    {
        var context = SyntheticVersionIdFilterTests.Context("GET", "UserLibrary", "GetItem", new() { ["itemId"] = _movie.Id });

        await SyntheticVersionIdFilterTests.Run(Create(Alice), context);

        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.GetItemById)));
    }

    public sealed class FakePlaybackInfoDto
    {
        public string? MediaSourceId { get; set; }
    }

    private sealed class FixedUrl(string value) : IInternalBaseUrl
    {
        public string Value => value;
    }
}
