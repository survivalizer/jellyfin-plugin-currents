using System.Text.Json;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public sealed class AdminUsersControllerTests : IDisposable
{
    private const string OverrideUrl = "https://aio.admin.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/overridepw/manifest.json";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioStreamsClient _streams = new();
    private readonly UserStore _users;

    public AdminUsersControllerTests() => _users = new UserStore(_settings, NullLogger<UserStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private AdminUsersController Create() =>
        new(new FixedDirectory([new DirectoryUser(Bob, "bob"), new DirectoryUser(Alice, "alice")]), _users, new StreamProfileResolver(_users, _settings), _streams);

    [Fact]
    public void Lists_every_jellyfin_user_with_masked_settings()
    {
        _users.Update(Alice, r => r.Self.AioStreamsManifestUrl = "https://aio.alice.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alicepw/manifest.json");

        var rows = Assert.IsAssignableFrom<IReadOnlyList<AdminUserRow>>(Assert.IsType<OkObjectResult>(Create().List().Result).Value);

        Assert.Equal(new[] { "alice", "bob" }, rows.Select(r => r.Name));
        Assert.Equal("aio.alice.example.com", rows[0].OwnConfigHost);
        Assert.Equal("Self", rows[0].Source);
        Assert.Equal("None", rows[1].Source);
        Assert.DoesNotContain("alicepw", JsonSerializer.Serialize(rows), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_sets_an_override_locks_and_disables()
    {
        var update = new AdminUserUpdate
        {
            AioStreamsManifestUrl = OverrideUrl,
            Preferences = new StreamPreferences { MaxSizeGb = 30 },
            AutoSelect = true,
            LockSelfService = true,
            SearchAutoAddDisabled = true,
        };

        var row = Assert.IsType<AdminUserRow>(Assert.IsType<OkObjectResult>((await Create().Update(Bob, update, CancellationToken.None)).Result).Value);

        Assert.Equal("Admin", row.Source);
        Assert.Equal("aio.admin.example.com", row.OverrideHost);
        Assert.True(row.LockSelfService);
        Assert.True(row.SearchAutoAddDisabled);
        Assert.Equal(30, _users.Get(Bob).Admin.Preferences!.MaxSizeGb);
        Assert.Equal(OverrideUrl, _users.Get(Bob).Admin.AioStreamsManifestUrl);

        var disabled = Assert.IsType<AdminUserRow>(Assert.IsType<OkObjectResult>((await Create().Update(Bob, new AdminUserUpdate { StreamsDisabled = true, LockSelfService = true }, CancellationToken.None)).Result).Value);
        Assert.True(disabled.StreamsDisabled);
        Assert.Equal("None", disabled.Source);
        Assert.Equal(OverrideUrl, _users.Get(Bob).Admin.AioStreamsManifestUrl);
    }

    [Fact]
    public async Task Unknown_users_and_invalid_configs_are_rejected()
    {
        _streams.Exception = new Jellyfin.Plugin.Currents.Clients.AioStreams.AioStreamsException("Invalid password");

        var unknown = await Create().Update(Guid.NewGuid(), new AdminUserUpdate(), CancellationToken.None);
        var invalid = await Create().Update(Bob, new AdminUserUpdate { AioStreamsManifestUrl = OverrideUrl }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(unknown.Result);
        Assert.Contains("Invalid password", ((StatusMessage)Assert.IsType<BadRequestObjectResult>(invalid.Result).Value!).Message, StringComparison.Ordinal);
        Assert.Null(_users.Get(Bob).Admin.AioStreamsManifestUrl);
    }

    private sealed class FixedDirectory(IReadOnlyList<DirectoryUser> users) : IUserDirectory
    {
        public IReadOnlyList<DirectoryUser> All() => users;
    }
}
