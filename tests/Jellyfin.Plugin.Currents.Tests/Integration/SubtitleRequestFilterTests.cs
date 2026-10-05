using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class SubtitleRequestFilterTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private readonly FakeAioStreamsClient _client = new();
    private readonly VersionCatalog _catalog;
    private readonly VersionRegistry _registry;
    private readonly Movie _movie;
    private readonly VersionEntry _version;
    private CompatState _compat;

    public SubtitleRequestFilterTests()
    {
        _compat = new CompatState(new Version(12, 1, 0), _settings);
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M", "M.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm };
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => (Guid)args[0]! == _movie.Id ? _movie : null);

        _registry = new VersionRegistry(_settings, _time);
        _version = new VersionEntry(
            StreamIdentity.VersionId(_movie.Id, Alice, Key, FakeSettings.Secret),
            _movie.Id,
            Alice,
            new CurrentsTitle("movie", "tt1"),
            new RankedStream(Key, new StreamResult { Url = "https://aio.example.com/play/a", Filename = "a.mkv", Size = 40_000_000_000 }));
        _registry.Register(_movie.Id, Alice, [_version]);

        _client.Outcome = new SearchOutcome([Offer("big", 40_000_000_000), Offer("small", 4_000_000_000)], []);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _catalog = new VersionCatalog(
            new StreamService(_client, _settings, new DiagnosticsLog(_time), _time, NullLogger<StreamService>.Instance),
            new StreamProfileResolver(users, _settings),
            _registry,
            _settings,
            new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
    }

    private static StreamResult Offer(string name, long size) => new()
    {
        Url = $"https://aio.example.com/play/{name}",
        Filename = $"{name}.mkv",
        Size = size,
        Duration = 7_200_000,
        ParsedFile = new ParsedFile { Resolution = "1080p", Encode = "AVC", AudioTags = ["AAC"] },
    };

    private async Task<VersionEntry> AliceVersion(long size) =>
        (await _catalog.GetAsync(_movie.Id, new CurrentsTitle("movie", "tt1"), Alice, TimeSpan.FromSeconds(10), CancellationToken.None))
            .Versions.Single(v => v.Stream.Result.Size == size);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SubtitleRequestFilter Create(Guid? user, bool apiKey = false)
    {
        var probes = new ProbeCache(_settings, _time);
        var builder = new VersionSourceBuilder(_settings, _time, probes, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
        var request = RequestContextTests.Create(RequestContextTests.Http(user, apiKey));
        return new SubtitleRequestFilter(_library.Instance, new CurrentsItemLocator(_settings, _time), _registry, _catalog, builder, request, _compat, _settings);
    }

    private Dictionary<string, object?> Subtitle(string sourceId, int index) => new()
    {
        ["routeItemId"] = _movie.Id,
        ["routeMediaSourceId"] = sourceId,
        ["routeIndex"] = index,
        ["routeFormat"] = "vtt",
        ["itemId"] = null,
        ["mediaSourceId"] = null,
        ["index"] = null,
        ["format"] = null,
    };

    // The result the filter set, or null when it let the request through.
    private static async Task<IActionResult?> Run(SubtitleRequestFilter filter, string action, Dictionary<string, object?> args)
    {
        var context = SyntheticVersionIdFilterTests.Context("GET", "Subtitle", action, args);
        var passed = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            passed = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });
        return passed ? null : context.Result;
    }

    [Theory]
    [InlineData(true, 2000)]
    [InlineData(true, 1000)]
    [InlineData(false, 2000)]
    public async Task Anonymous_requests_for_a_currents_subtitle_get_404(bool versionId, int index)
    {
        var source = versionId ? _version.VersionId : _movie.Id.ToString("N");

        Assert.IsType<NotFoundResult>(await Run(Create(null), "GetSubtitle", Subtitle(source, index)));
    }

    [Fact]
    public async Task Builtin_subtitles_over_the_limit_get_404()
    {
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", Subtitle(_version.VersionId, 2)));
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitleWithTicks", Subtitle(_version.VersionId, 3)));
        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(_version.VersionId, 1000)));
        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(_version.VersionId, 2000)));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(1000)]
    public async Task Another_users_version_gets_404(int index)
    {
        var bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.IsType<NotFoundResult>(await Run(Create(bob), "GetSubtitle", Subtitle(_version.VersionId, index)));
    }

    [Fact]
    public async Task An_api_key_caller_may_reach_any_version()
    {
        _settings.Current.EmbeddedSubtitleMaxGb = 0;
        Assert.Null(await Run(Create(null, apiKey: true), "GetSubtitle", Subtitle(_version.VersionId, 2)));

        _settings.Current.EmbeddedSubtitleMaxGb = 15;
        Assert.IsType<NotFoundResult>(await Run(Create(null, apiKey: true), "GetSubtitle", Subtitle(_version.VersionId, 2)));
    }

    [Fact]
    public async Task An_expired_version_is_looked_up_again_and_still_guarded()
    {
        var big = await AliceVersion(40_000_000_000);
        _time.Advance(TimeSpan.FromHours(25));
        Assert.False(_registry.TryGet(big.VersionId, out _));

        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", Subtitle(big.VersionId, 2)));
        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(big.VersionId, 2000)));
        Assert.True(_registry.TryGet(big.VersionId, out _));
    }

    [Fact]
    public async Task A_small_version_keeps_its_builtin_subtitles_after_a_restart()
    {
        var small = await AliceVersion(4_000_000_000);
        _time.Advance(TimeSpan.FromHours(25));

        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(small.VersionId, 2)));
    }

    [Fact]
    public async Task Ids_that_name_no_version_of_this_item_get_404()
    {
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", Subtitle("ffffffffffffffffffffffffffffffff", 2000)));

        var otherItem = Subtitle(_version.VersionId, 2000);
        otherItem["routeItemId"] = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", otherItem));
    }

    [Fact]
    public async Task An_item_id_source_names_no_version()
    {
        var itemSource = _movie.Id.ToString("N");

        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", Subtitle(itemSource, 2000)));
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", Subtitle(itemSource, 2)));
        Assert.IsType<NotFoundResult>(await Run(Create(null, apiKey: true), "GetSubtitle", Subtitle(itemSource, 2000)));
    }

    [Fact]
    public async Task The_item_id_of_a_user_without_versions_gets_404()
    {
        var bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.IsType<NotFoundResult>(await Run(Create(bob), "GetSubtitle", Subtitle(_movie.Id.ToString("N"), 2000)));
    }

    [Fact]
    public async Task Anonymous_callers_never_trigger_a_search()
    {
        Assert.IsType<NotFoundResult>(await Run(Create(null), "GetSubtitle", Subtitle("ffffffffffffffffffffffffffffffff", 2000)));
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task Api_key_callers_never_trigger_a_search()
    {
        Assert.IsType<NotFoundResult>(await Run(Create(null, apiKey: true), "GetSubtitle", Subtitle("ffffffffffffffffffffffffffffffff", 2000)));
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task Builtin_subtitles_pass_without_a_limit()
    {
        _settings.Current.EmbeddedSubtitleMaxGb = 0;

        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(_version.VersionId, 2)));
    }

    [Fact]
    public async Task Query_arguments_win_over_route_ones()
    {
        // Route names a passing request (downloaded subtitle), query a refused one (built-in track over the limit).
        var queryRefuses = Subtitle(_version.VersionId, 2000);
        queryRefuses["mediaSourceId"] = _version.VersionId;
        queryRefuses["index"] = 2;
        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", queryRefuses));

        // The converse: route refused, query passing.
        var queryPasses = Subtitle(_version.VersionId, 2);
        queryPasses["mediaSourceId"] = _version.VersionId;
        queryPasses["index"] = 2000;
        Assert.Null(await Run(Create(Alice), "GetSubtitle", queryPasses));
    }

    [Fact]
    public async Task The_hls_subtitle_playlist_is_guarded_too()
    {
        var args = new Dictionary<string, object?> { ["itemId"] = _movie.Id, ["index"] = 2, ["mediaSourceId"] = _version.VersionId, ["segmentLength"] = 30 };

        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitlePlaylist", args));
    }

    [Fact]
    public async Task Other_items_and_an_inactive_guard_are_left_alone()
    {
        var other = Subtitle(Guid.NewGuid().ToString("N"), 2);
        other["routeItemId"] = Guid.NewGuid();
        Assert.Null(await Run(Create(null), "GetSubtitle", other));

        _compat = new CompatState(new Version(13, 0, 0), _settings);
        Assert.Null(await Run(Create(null), "GetSubtitle", Subtitle(_version.VersionId, 2)));
    }
}
