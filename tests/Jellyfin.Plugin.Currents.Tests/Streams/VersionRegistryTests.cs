using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class VersionRegistryTests
{
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly FakeSettings _settings = new();

    private static VersionEntry Entry(Guid user, string key) =>
        new(StreamIdentity.VersionId(Item, user, key), Item, user, new CurrentsTitle("movie", "tt1"), new RankedStream(key, new StreamResult { Url = $"https://x/{key}" }));

    private VersionRegistry Create() => new(_settings, _time);

    [Fact]
    public void Finds_versions_by_id_and_maps_them_to_the_base_item()
    {
        var registry = Create();
        var entry = Entry(Alice, "k1");
        registry.Register(Item, Alice, [entry]);

        Assert.True(registry.TryGet(entry.VersionId, out var found));
        Assert.Equal(entry, found);
        Assert.True(registry.TryGetBaseItemId(Guid.ParseExact(entry.VersionId, "N"), out var baseId));
        Assert.Equal(Item, baseId);
        Assert.False(registry.TryGetBaseItemId(Guid.NewGuid(), out _));
    }

    [Fact]
    public void Lists_per_user_and_latest_for_unknown_users()
    {
        var registry = Create();
        registry.Register(Item, Alice, [Entry(Alice, "a")]);
        registry.Register(Item, Bob, [Entry(Bob, "b")]);

        Assert.Equal("a", Assert.Single(registry.ForItem(Item, Alice)).Stream.Key);
        Assert.Equal("b", Assert.Single(registry.ForItem(Item, Bob)).Stream.Key);
        Assert.Equal("b", Assert.Single(registry.ForItem(Item, Guid.Empty)).Stream.Key);
        Assert.Empty(registry.ForItem(Guid.NewGuid(), Alice));
    }

    [Fact]
    public void Replaced_lists_keep_old_ids_resolvable_until_expiry()
    {
        _settings.Current.VersionTokenHours = 24;
        var registry = Create();
        var old = Entry(Alice, "old");
        registry.Register(Item, Alice, [old]);
        registry.Register(Item, Alice, [Entry(Alice, "new")]);

        Assert.True(registry.TryGet(old.VersionId, out _));
        Assert.Equal("new", Assert.Single(registry.ForItem(Item, Alice)).Stream.Key);

        _time.Advance(TimeSpan.FromHours(25));

        Assert.False(registry.TryGet(old.VersionId, out _));
        Assert.Empty(registry.ForItem(Item, Alice));
    }
}
