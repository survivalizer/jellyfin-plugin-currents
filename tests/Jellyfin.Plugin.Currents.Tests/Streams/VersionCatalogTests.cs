using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public sealed class VersionCatalogTests : IDisposable
{
    private const string AliceUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alice/manifest.json";
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly CurrentsTitle Title = new("movie", "tt1");
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeAioStreamsClient _client = new();
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly UserStore _users;
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;

    public VersionCatalogTests()
    {
        _client.Outcome = new SearchOutcome(
            Enumerable.Range(1, 30).Select(i => new StreamResult { Url = $"https://cdn.example.com/{i}", Filename = $"f{i}.mkv" }).ToList(),
            []);
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _users.Update(Alice, r => r.Self.AioStreamsManifestUrl = AliceUrl);
        _registry = new VersionRegistry(_settings, _time);
        _catalog = new VersionCatalog(
            new StreamService(_client, _settings, _time, NullLogger<StreamService>.Instance),
            new StreamProfileResolver(_users, _settings),
            _registry,
            _settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    [Fact]
    public async Task Builds_at_most_max_versions_with_per_user_ids_and_registers_them()
    {
        _settings.Current.MaxVersions = 5;

        var list = await _catalog.GetAsync(Item, Title, Alice, Wait, CancellationToken.None);

        Assert.Null(list.Notice);
        Assert.Equal(5, list.Versions.Count);
        Assert.All(list.Versions, v => Assert.Equal(StreamIdentity.VersionId(Item, Alice, v.Stream.Key), v.VersionId));
        Assert.Equal("f1.mkv", list.Versions[0].Stream.Result.Filename);
        Assert.True(_registry.TryGet(list.Versions[4].VersionId, out var entry));
        Assert.Equal(new VersionTicket(Alice, "movie", "tt1", list.Versions[4].Stream.Key), entry.Ticket);
    }

    [Fact]
    public async Task Auto_select_exposes_only_the_best_stream()
    {
        _users.Update(Alice, r => r.Self.AutoSelect = true);

        var list = await _catalog.GetAsync(Item, Title, Alice, Wait, CancellationToken.None);

        Assert.Single(list.Versions);
    }

    [Fact]
    public async Task Unconfigured_user_gets_a_notice_and_nothing_is_registered()
    {
        var list = await _catalog.GetAsync(Item, Title, Bob, Wait, CancellationToken.None);

        Assert.Empty(list.Versions);
        Assert.Contains("not configured", list.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_registry.ForItem(Item, Bob));
        Assert.Equal(0, _client.Calls);
    }

    [Fact]
    public async Task Peek_is_null_when_cold_and_never_searches()
    {
        Assert.Null(_catalog.Peek(Item, Title, Alice));
        Assert.Equal(0, _client.Calls);

        await _catalog.GetAsync(Item, Title, Alice, Wait, CancellationToken.None);

        Assert.Equal(20, _catalog.Peek(Item, Title, Alice)!.Versions.Count);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task Two_users_on_different_configs_get_disjoint_version_ids()
    {
        _settings.Current.AioStreamsManifestUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/default/manifest.json";

        var alice = await _catalog.GetAsync(Item, Title, Alice, Wait, CancellationToken.None);
        var bob = await _catalog.GetAsync(Item, Title, Bob, Wait, CancellationToken.None);

        Assert.Empty(alice.Versions.Select(v => v.VersionId).Intersect(bob.Versions.Select(v => v.VersionId)));
        Assert.Equal(2, _client.Calls);
    }
}
