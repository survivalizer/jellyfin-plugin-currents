using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public sealed class UserSettingsControllerTests : IDisposable
{
    private const string AliceUrl = "https://aio.alice.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alicepw/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _streams = new();
    private readonly UserStore _users;

    public UserSettingsControllerTests() => _users = new UserStore(_settings, NullLogger<UserStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    internal static ControllerContext As(Guid? user)
    {
        var claims = user is { } id ? new[] { new Claim(JellyfinClaims.UserIdClaim, id.ToString("N")) } : [];
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Custom")) } };
    }

    private readonly ListLogger<UserSettingsController> _logger = new();

    private UserSettingsController Create(Guid? user) =>
        new(_users, new StreamProfileResolver(_users, _settings), _streams, _settings, _logger) { ControllerContext = As(user) };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task User_saves_their_own_config_and_preferences_and_never_gets_the_url_back()
    {
        var update = new UserSettingsUpdate { AioStreamsManifestUrl = AliceUrl, Preferences = new StreamPreferences { ResolutionOrder = ["1080p"] }, AutoSelect = true };

        var saved = Ok(await Create(Alice).UpdateSettings(update, CancellationToken.None));

        Assert.Equal("Self", saved.Source);
        Assert.Equal("aio.alice.example.com", saved.ConfigHost);
        Assert.Equal(new[] { "1080p" }, saved.EffectivePreferences.ResolutionOrder);
        Assert.True(saved.EffectiveAutoSelect);
        Assert.Equal(AliceUrl, _users.Get(Alice).Self.AioStreamsManifestUrl);
        Assert.Equal(1, _streams.Calls);
        Assert.DoesNotContain("alicepw", JsonSerializer.Serialize(saved), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_keeps_and_empty_removes()
    {
        await Create(Alice).UpdateSettings(new UserSettingsUpdate { AioStreamsManifestUrl = AliceUrl, AutoSelect = true }, CancellationToken.None);

        await Create(Alice).UpdateSettings(new UserSettingsUpdate { Preferences = new StreamPreferences { CachedOnly = true } }, CancellationToken.None);
        Assert.Equal(AliceUrl, _users.Get(Alice).Self.AioStreamsManifestUrl);
        Assert.True(_users.Get(Alice).Self.AutoSelect);

        var cleared = Ok(await Create(Alice).UpdateSettings(new UserSettingsUpdate { AioStreamsManifestUrl = string.Empty, ClearAutoSelect = true, ClearPreferences = true }, CancellationToken.None));
        Assert.Null(_users.Get(Alice).Self.AioStreamsManifestUrl);
        Assert.Null(_users.Get(Alice).Self.AutoSelect);
        Assert.Null(_users.Get(Alice).Self.Preferences);
        Assert.Null(cleared.ConfigHost);
    }

    [Fact]
    public async Task Invalid_config_is_rejected_with_a_masked_reason_and_not_saved()
    {
        _streams.Exception = new AioStreamsException("Invalid password for https://aio.alice.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alicepw/manifest.json");

        var result = await Create(Alice).UpdateSettings(new UserSettingsUpdate { AioStreamsManifestUrl = AliceUrl }, CancellationToken.None);
        var unparsable = await Create(Alice).UpdateSettings(new UserSettingsUpdate { AioStreamsManifestUrl = "https://aio.example.com/stremio/u/alias" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("AIOStreams did not accept this config. Check the URL and try again.", ((StatusMessage)bad.Value!).Message);
        var unparsableMessage = ((StatusMessage)Assert.IsType<BadRequestObjectResult>(unparsable.Result).Value!).Message;
        Assert.DoesNotContain("did not accept", unparsableMessage, StringComparison.Ordinal);
        Assert.Null(_users.Get(Alice).Self.AioStreamsManifestUrl);
        var logged = Assert.Single(_logger.Entries).Message;
        Assert.Contains("Invalid password", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("alicepw", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Locked_users_and_disabled_self_service_cannot_edit()
    {
        _users.Update(Alice, r => r.LockSelfService = true);

        var locked = await Create(Alice).UpdateSettings(new UserSettingsUpdate { AutoSelect = true }, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(locked.Result).StatusCode);
        Assert.False(Ok(Create(Alice).GetSettings()).CanEdit);

        _users.Update(Alice, r => r.LockSelfService = false);
        _settings.Current.AllowSelfService = false;
        var off = await Create(Alice).UpdateSettings(new UserSettingsUpdate { AutoSelect = true }, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(off.Result).StatusCode);
        Assert.Null(_users.Get(Alice).Self.AutoSelect);
    }

    [Fact]
    public async Task Api_keys_and_anonymous_callers_have_no_settings()
    {
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(Create(null).GetSettings().Result).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>((await Create(Guid.Empty).UpdateSettings(new UserSettingsUpdate(), CancellationToken.None)).Result).StatusCode);
    }

    [Fact]
    public void Disabled_user_sees_that_streams_are_off()
    {
        _users.Update(Alice, r => r.StreamsDisabled = true);

        var settings = Ok(Create(Alice).GetSettings());

        Assert.True(settings.StreamsDisabled);
        Assert.Equal("None", settings.Source);
    }

    [Fact]
    public async Task User_turns_search_add_off_and_back_to_the_default()
    {
        var off = Ok(await Create(Alice).UpdateSettings(new UserSettingsUpdate { SearchAutoAdd = false }, CancellationToken.None));
        Assert.False(off.SearchAutoAdd);
        Assert.False(off.EffectiveSearchAutoAdd);
        Assert.True(off.SearchAvailable);
        Assert.False(_users.Get(Alice).Self.SearchAutoAdd);

        var reset = Ok(await Create(Alice).UpdateSettings(new UserSettingsUpdate { ClearSearchAutoAdd = true }, CancellationToken.None));
        Assert.Null(reset.SearchAutoAdd);
        Assert.True(reset.EffectiveSearchAutoAdd);
    }

    [Fact]
    public void Response_reports_search_off_by_admin_and_globally()
    {
        _users.Update(Alice, r => r.SearchAutoAddDisabled = true);
        var settings = Ok(Create(Alice).GetSettings());
        Assert.True(settings.SearchAddDisabledByAdmin);
        Assert.False(settings.EffectiveSearchAutoAdd);

        _settings.Current.EnableSearch = false;
        Assert.False(Ok(Create(Alice).GetSettings()).SearchAvailable);
    }
}
