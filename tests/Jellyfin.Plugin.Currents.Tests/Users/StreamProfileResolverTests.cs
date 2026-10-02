using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Users;

public sealed class StreamProfileResolverTests : IDisposable
{
    private const string DefaultUrl = "https://aio.example.com/stremio/00000000-0000-4000-8000-000000000001/default/manifest.json";
    private const string AdminUrl = "https://aio.example.com/stremio/00000000-0000-4000-8000-000000000002/admin/manifest.json";
    private const string SelfUrl = "https://aio.example.com/stremio/00000000-0000-4000-8000-000000000003/self/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly UserStore _users;

    public StreamProfileResolverTests()
    {
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _settings.Current.AioStreamsManifestUrl = DefaultUrl;
    }

    private StreamProfile For(Guid? user) => new StreamProfileResolver(_users, _settings).For(user);

    private StreamProfileResolver Resolver() => new(_users, _settings);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    [Fact]
    public void No_user_uses_the_default()
    {
        var profile = For(null);

        Assert.Equal(ProfileSource.Default, profile.Source);
        Assert.Equal("default", profile.Credentials!.Password);
        Assert.True(profile.CanPlay);
    }

    [Fact]
    public void Admin_override_beats_default()
    {
        _users.Update(Alice, r => r.Admin.AioStreamsManifestUrl = AdminUrl);

        Assert.Equal(ProfileSource.Admin, For(Alice).Source);
        Assert.Equal(ProfileSource.Default, For(Guid.NewGuid()).Source);
    }

    [Fact]
    public void Self_service_beats_admin_override()
    {
        _users.Update(Alice, r =>
        {
            r.Admin.AioStreamsManifestUrl = AdminUrl;
            r.Self.AioStreamsManifestUrl = SelfUrl;
        });

        Assert.Equal(ProfileSource.Self, For(Alice).Source);
        Assert.Equal("self", For(Alice).Credentials!.Password);
    }

    [Fact]
    public void Locked_user_ignores_their_own_config()
    {
        _users.Update(Alice, r =>
        {
            r.Admin.AioStreamsManifestUrl = AdminUrl;
            r.Self.AioStreamsManifestUrl = SelfUrl;
            r.LockSelfService = true;
        });

        Assert.Equal(ProfileSource.Admin, For(Alice).Source);
    }

    [Fact]
    public void Self_service_switched_off_globally_ignores_user_configs()
    {
        _settings.Current.AllowSelfService = false;
        _users.Update(Alice, r => r.Self.AioStreamsManifestUrl = SelfUrl);

        Assert.Equal(ProfileSource.Default, For(Alice).Source);
    }

    [Fact]
    public void Disabled_user_cannot_play_even_with_configs()
    {
        _users.Update(Alice, r =>
        {
            r.Self.AioStreamsManifestUrl = SelfUrl;
            r.StreamsDisabled = true;
        });

        var profile = For(Alice);

        Assert.True(profile.Disabled);
        Assert.Null(profile.Credentials);
        Assert.False(profile.CanPlay);
    }

    [Fact]
    public void Nothing_configured_means_none()
    {
        _settings.Current.AioStreamsManifestUrl = string.Empty;

        var profile = For(Alice);

        Assert.Equal(ProfileSource.None, profile.Source);
        Assert.False(profile.Disabled);
        Assert.False(profile.CanPlay);
    }

    [Fact]
    public void Invalid_url_at_a_layer_falls_through_to_the_next()
    {
        _users.Update(Alice, r => r.Self.AioStreamsManifestUrl = "https://aio.example.com/stremio/u/alias");

        Assert.Equal(ProfileSource.Default, For(Alice).Source);
    }

    [Fact]
    public void Preferences_and_auto_select_resolve_field_by_field()
    {
        _settings.Current.DefaultPreferences = new StreamPreferences { ResolutionOrder = ["720p"] };
        _settings.Current.DefaultAutoSelect = false;
        _users.Update(Alice, r =>
        {
            r.Admin.Preferences = new StreamPreferences { ResolutionOrder = ["2160p"] };
            r.Self.AutoSelect = true;
        });

        var alice = For(Alice);
        var bob = For(Guid.NewGuid());

        Assert.Equal(new[] { "2160p" }, alice.Preferences.ResolutionOrder);
        Assert.True(alice.AutoSelect);
        Assert.Equal(ProfileSource.Default, alice.Source);
        Assert.Equal(new[] { "720p" }, bob.Preferences.ResolutionOrder);
        Assert.False(bob.AutoSelect);
    }

    [Fact]
    public void Search_auto_add_follows_the_default_when_nothing_is_set()
    {
        Assert.True(Resolver().SearchAutoAdd(Alice));
        _settings.Current.DefaultSearchAutoAdd = false;
        Assert.False(Resolver().SearchAutoAdd(Alice));
    }

    [Fact]
    public void Search_auto_add_self_service_beats_admin_layer_and_default()
    {
        _settings.Current.DefaultSearchAutoAdd = false;
        _users.Update(Alice, r =>
        {
            r.Admin.SearchAutoAdd = false;
            r.Self.SearchAutoAdd = true;
        });

        Assert.True(Resolver().SearchAutoAdd(Alice));
    }

    [Fact]
    public void Search_auto_add_ignores_self_service_when_locked_or_not_allowed()
    {
        _users.Update(Alice, r => r.Self.SearchAutoAdd = false);
        Assert.False(Resolver().SearchAutoAdd(Alice));

        _settings.Current.AllowSelfService = false;
        Assert.True(Resolver().SearchAutoAdd(Alice));

        _settings.Current.AllowSelfService = true;
        _users.Update(Alice, r => r.LockSelfService = true);
        Assert.True(Resolver().SearchAutoAdd(Alice));
    }

    [Fact]
    public void Search_auto_add_is_off_when_the_admin_disabled_it_or_search_is_off()
    {
        _users.Update(Alice, r => r.Self.SearchAutoAdd = true);

        _settings.Current.EnableSearch = false;
        Assert.False(Resolver().SearchAutoAdd(Alice));

        _settings.Current.EnableSearch = true;
        _users.Update(Alice, r => r.SearchAutoAddDisabled = true);
        Assert.False(Resolver().SearchAutoAdd(Alice));
    }

    [Fact]
    public void Search_auto_add_is_off_for_no_user()
    {
        Assert.False(Resolver().SearchAutoAdd(Guid.Empty));
    }
}
