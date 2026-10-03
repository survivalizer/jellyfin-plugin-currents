using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaSegments;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class SegmentRequestFilterTests : IDisposable
{
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly CurrentsTitle Title = new("movie", "tt1");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _client = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly VersionSourceBuilder _builder;
    private readonly SegmentStore _store;
    private readonly Movie _movie;

    public SegmentRequestFilterTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M", "M.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm, RunTimeTicks = TimeSpan.FromMinutes(120).Ticks };
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => (Guid)args[0]! == _movie.Id ? _movie : null);

        // Ranked first: a 100-minute 2160p cut; second: the 120-minute cut the markers belong to.
        _client.Outcome = new SearchOutcome([Result("a", "2160p", 6_000_000), Result("b", "1080p", 7_200_000)], []);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        var remux = new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(new StreamService(_client, _settings, new Jellyfin.Plugin.Currents.Common.DiagnosticsLog(_time), _time, NullLogger<StreamService>.Instance), new StreamProfileResolver(users, _settings), _registry, _settings, remux);
        _builder = new VersionSourceBuilder(_settings, _time, new ProbeCache(_settings, _time), remux);
        _store = new SegmentStore(_settings, _time);
        _store.Set(Title, new SegmentLookup([new SkipMarker(MarkerKind.Intro, 0, 5_000)], TimeSpan.FromMinutes(120).Ticks));
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static StreamResult Result(string name, string resolution, double durationMs) => new()
    {
        Url = $"https://aio.example.com/play/{name}",
        Filename = $"{name}.mkv",
        Duration = durationMs,
        ParsedFile = new ParsedFile { Resolution = resolution, Encode = "AVC" },
    };

    private SegmentRequestFilter Create() => new(
        _registry,
        _library.Instance,
        new CurrentsItemLocator(_settings, _time),
        new SegmentGate(_store, _settings),
        _builder,
        RequestContextTests.Create(RequestContextTests.Http(Alice, action: ("MediaSegments", "GetItemSegments"))),
        _settings);

    private static ActionExecutingContext Segments(Guid id) =>
        SyntheticVersionIdFilterTests.Context("GET", "MediaSegments", "GetItemSegments", new() { ["itemId"] = id, ["includeSegmentTypes"] = null });

    private static bool Blocked(ActionExecutingContext context) =>
        context.Result is OkObjectResult { Value: QueryResult<MediaSegmentDto> { Items.Count: 0 } };

    private async Task<IReadOnlyList<VersionEntry>> AliceVersions() =>
        (await _catalog.GetAsync(_movie.Id, Title, Alice, TimeSpan.FromSeconds(10), CancellationToken.None)).Versions;

    [Fact]
    public async Task A_version_within_the_tolerance_passes()
    {
        var versions = await AliceVersions();
        var context = Segments(Guid.Parse(versions[1].VersionId));

        await SyntheticVersionIdFilterTests.Run(Create(), context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task A_version_of_another_length_gets_an_empty_list()
    {
        var versions = await AliceVersions();
        var context = Segments(Guid.Parse(versions[0].VersionId));

        await SyntheticVersionIdFilterTests.Run(Create(), context);

        Assert.True(Blocked(context));
    }

    [Fact]
    public async Task The_item_id_is_judged_by_the_users_top_version()
    {
        await AliceVersions();
        var context = Segments(_movie.Id);

        await SyntheticVersionIdFilterTests.Run(Create(), context);

        Assert.True(Blocked(context));
    }

    [Fact]
    public async Task Turning_markers_off_hides_stored_markers()
    {
        var versions = await AliceVersions();
        _settings.Current.EnableSegments = false;
        var context = Segments(Guid.Parse(versions[1].VersionId));

        await SyntheticVersionIdFilterTests.Run(Create(), context);

        Assert.True(Blocked(context));
    }

    [Fact]
    public async Task Degraded_mode_follows_the_unknown_runtime_setting()
    {
        _settings.Current.EnableVersions = false;
        var blocked = Segments(_movie.Id);
        await SyntheticVersionIdFilterTests.Run(Create(), blocked);
        Assert.True(Blocked(blocked));

        _settings.Current.SegmentsWhenRuntimeUnknown = true;
        var allowed = Segments(_movie.Id);
        await SyntheticVersionIdFilterTests.Run(Create(), allowed);
        Assert.Null(allowed.Result);
    }

    [Fact]
    public async Task Other_items_and_actions_pass()
    {
        var versions = await AliceVersions();
        var other = Segments(Guid.NewGuid());
        var item = SyntheticVersionIdFilterTests.Context("GET", "UserLibrary", "GetItem", new() { ["itemId"] = Guid.Parse(versions[0].VersionId) });

        await SyntheticVersionIdFilterTests.Run(Create(), other);
        await SyntheticVersionIdFilterTests.Run(Create(), item);

        Assert.Null(other.Result);
        Assert.Null(item.Result);
    }
}
