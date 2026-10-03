using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
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
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SubtitleRequestFilter Create(Guid? user)
    {
        var probes = new ProbeCache(_settings, _time);
        var builder = new VersionSourceBuilder(_settings, _time, probes, new RemuxDbCache(new FakeRemuxDbClient(), _settings, _time, NullLogger<RemuxDbCache>.Instance));
        var request = RequestContextTests.Create(RequestContextTests.Http(user));
        return new SubtitleRequestFilter(_library.Instance, new CurrentsItemLocator(_settings, _time), _registry, builder, request, _compat, _settings);
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

    [Fact]
    public async Task Builtin_subtitles_pass_without_a_limit()
    {
        _settings.Current.EmbeddedSubtitleMaxGb = 0;

        Assert.Null(await Run(Create(Alice), "GetSubtitle", Subtitle(_version.VersionId, 2)));
    }

    [Fact]
    public async Task Query_arguments_win_over_route_ones()
    {
        var args = Subtitle("ffffffffffffffffffffffffffffffff", 2000);
        args["mediaSourceId"] = _version.VersionId;
        args["index"] = 2;

        Assert.IsType<NotFoundResult>(await Run(Create(Alice), "GetSubtitle", args));
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
