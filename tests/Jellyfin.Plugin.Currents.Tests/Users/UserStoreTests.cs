using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Users;

public sealed class UserStoreTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();

    private UserStore Create() => new(_settings, NullLogger<UserStore>.Instance);

    private string FilePath => Path.Combine(_settings.DataFolderPath, "users.json");

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    [Fact]
    public void Unknown_user_gets_an_empty_record()
    {
        var record = Create().Get(Alice);

        Assert.Null(record.Self.AioStreamsManifestUrl);
        Assert.Null(record.Admin.Preferences);
        Assert.False(record.StreamsDisabled);
    }

    [Fact]
    public void Updates_persist_across_instances()
    {
        Create().Update(Alice, r =>
        {
            r.Self.AioStreamsManifestUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
            r.Self.Preferences = new StreamPreferences { ResolutionOrder = ["1080p"] };
            r.LockSelfService = true;
        });

        var record = Create().Get(Alice);

        Assert.EndsWith("/pw/manifest.json", record.Self.AioStreamsManifestUrl, StringComparison.Ordinal);
        Assert.Equal(new[] { "1080p" }, record.Self.Preferences!.ResolutionOrder);
        Assert.True(record.LockSelfService);
        Assert.Single(Create().All());
    }

    [Fact]
    public void Get_returns_a_copy()
    {
        var store = Create();
        store.Update(Alice, r => r.Self.AutoSelect = true);

        store.Get(Alice).Self.AutoSelect = false;

        Assert.True(store.Get(Alice).Self.AutoSelect);
    }

    [Fact]
    public void Remove_deletes_the_record()
    {
        var store = Create();
        store.Update(Alice, r => r.StreamsDisabled = true);

        Assert.True(store.Remove(Alice));
        Assert.False(Create().Get(Alice).StreamsDisabled);
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_store_starts_empty()
    {
        Directory.CreateDirectory(_settings.DataFolderPath);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(Create().All());
        Assert.Single(Directory.GetFiles(_settings.DataFolderPath, "users.json.corrupt-*"));
    }

    [Fact]
    public void Nulls_and_bad_keys_in_the_file_are_tolerated()
    {
        Directory.CreateDirectory(_settings.DataFolderPath);
        File.WriteAllText(FilePath, """
            {
              "not-a-guid": { "streamsDisabled": true },
              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": { "admin": null, "self": { "preferences": { "resolutionOrder": null } } },
              "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb": null
            }
            """);

        var record = Create().Get(Alice);

        Assert.NotNull(record.Admin);
        Assert.Empty(record.Self.Preferences!.ResolutionOrder);
        Assert.Single(Create().All());
    }

    [Fact]
    public void File_is_private_to_the_jellyfin_user_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Create().Update(Alice, r => r.Self.AioStreamsManifestUrl = "secret");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
    }

    [Fact]
    public async Task Concurrent_updates_are_not_lost()
    {
        var store = Create();
        var users = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(users.Select(u => Task.Run(() => store.Update(u, r => r.Self.AutoSelect = true))));

        Assert.Equal(20, Create().All().Count);
    }
}
