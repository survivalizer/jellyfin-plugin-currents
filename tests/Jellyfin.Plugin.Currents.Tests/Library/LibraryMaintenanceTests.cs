using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public sealed class LibraryMaintenanceTests : IDisposable
{
    private static readonly ManualTimeProvider Time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Gone = Guid.Parse("cccccccccccccccccccccccccccccccc");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "currents-maint-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSettings _settings;
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeLibraryRefresher _refresher = new();
    private readonly FakeLibraryItems _items = new();
    private readonly FakeDirectory _directory = new();
    private readonly CountingStreams _streams = new();
    private readonly TitleLibrary _titles;
    private readonly UserStore _users;
    private readonly LibraryJobGate _jobs = new();

    public LibraryMaintenanceTests()
    {
        _settings = new FakeSettings { DataFolderPath = Path.Combine(_root, "data") };
        _settings.Current.LibraryRoot = Path.Combine(_root, "library");
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _titles = new TitleLibrary(_settings, Time, NullLogger<TitleLibrary>.Instance);
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _directory.Users.Add(new DirectoryUser(Alice, "alice"));
    }

    public void Dispose()
    {
        _jobs.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LibraryMaintenance Create() => new(
        _client,
        _titles,
        _refresher,
        _items,
        _users,
        _directory,
        _streams,
        new ProbeCache(_settings, Time),
        new SegmentStore(_settings, Time),
        _jobs,
        _settings,
        NullLogger<LibraryMaintenance>.Instance);

    private string Folder(TitleState title) => Path.Combine(_settings.Current.LibraryRoot, title.Folder);

    private TitleState AddMovie(string id, string name, string catalog = "movie/tmdb.top")
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", id);
        var meta = new StremioMeta { Id = id, Name = name, Year = "2000" };
        var result = _titles.CreateWriter().WriteMovie(key, meta, null);
        var title = new TitleState { StateId = key.StateId, Kind = MediaKind.Movie, StremioId = id, Folder = result.RelativeFolder, Catalogs = [catalog] };
        _titles.Use(state =>
        {
            state.Upsert(title);
            state.Save();
        });
        _client.Metas["movie/" + id] = meta;
        return title;
    }

    [Fact]
    public async Task Rewrites_titles_whose_files_are_missing_and_refreshes()
    {
        var kept = AddMovie("tt1", "Alpha");
        var lost = AddMovie("tt2", "Beta");
        Directory.Delete(Folder(lost), recursive: true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(new VerifyReport(1, 0, 0), report);
        Assert.True(File.Exists(Path.Combine(Folder(lost), "Beta (2000).strm")));
        Assert.True(Directory.Exists(Folder(kept)));
        Assert.DoesNotContain("movie/tt1", _client.MetaRequests);
        Assert.Single(_refresher.Refreshed);
    }

    [Fact]
    public async Task Anime_titles_fall_back_to_their_catalog_type()
    {
        var lost = AddMovie("tt3", "Gamma", catalog: "anime.movie/mal.top");
        _client.Metas.Remove("movie/tt3");
        _client.Metas["anime.movie/tt3"] = new StremioMeta { Id = "tt3", Name = "Gamma", Year = "2000" };
        Directory.Delete(Folder(lost), recursive: true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, report.Rewritten);
        Assert.Equal(new[] { "movie/tt3", "anime.movie/tt3" }, _client.MetaRequests);
    }

    [Fact]
    public async Task Titles_that_cannot_be_fetched_count_as_failed_and_stay_in_state()
    {
        var lost = AddMovie("tt4", "Delta");
        _client.Metas.Remove("movie/tt4");
        Directory.Delete(Folder(lost), recursive: true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(new VerifyReport(0, 1, 0), report);
        Assert.NotNull(_titles.Get(lost.StateId));
        Assert.Empty(_refresher.Refreshed);
    }

    [Fact]
    public async Task Titles_on_an_unmounted_root_are_left_alone()
    {
        AddMovie("tt5", "Epsilon");
        Directory.Delete(Path.Combine(_settings.Current.LibraryRoot, "Movies"), recursive: true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(new VerifyReport(0, 0, 0), report);
        Assert.False(Directory.Exists(Path.Combine(_settings.Current.LibraryRoot, "Movies")));
    }

    [Fact]
    public async Task Forgets_records_of_deleted_users_only()
    {
        _users.Update(Alice, r => r.LockSelfService = true);
        _users.Update(Gone, r => r.LockSelfService = true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, report.UsersRemoved);
        Assert.Equal(new[] { Alice }, _users.All().Keys);
    }

    [Fact]
    public async Task An_empty_user_directory_removes_nobody()
    {
        _directory.Users.Clear();
        _users.Update(Gone, r => r.LockSelfService = true);

        var report = await Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(0, report.UsersRemoved);
        Assert.Single(_users.All());
    }

    [Fact]
    public async Task Purge_removes_every_title_the_state_and_the_caches()
    {
        var alpha = AddMovie("tt1", "Alpha");
        var userFile = Path.Combine(Folder(alpha), "notes.txt");
        File.WriteAllText(userFile, "mine");
        var beta = AddMovie("tt2", "Beta");
        var alphaItem = Guid.NewGuid();
        var betaItem = Guid.NewGuid();
        _items.Titles[alpha.StateId] = alphaItem;
        _items.Titles[beta.StateId] = betaItem;
        var probes = Path.Combine(_settings.DataFolderPath, "probes");
        Directory.CreateDirectory(probes);
        File.WriteAllText(Path.Combine(probes, "x.json"), "{}");
        new SegmentStore(_settings, Time).Set(new CurrentsTitle("movie", "tt1"), SegmentLookup.None);

        var removed = await Create().PurgeAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(Folder(beta)));
        Assert.False(File.Exists(Path.Combine(Folder(alpha), "Alpha (2000).strm")));
        Assert.True(File.Exists(userFile));
        Assert.Equal(0, _titles.Use(s => s.Titles.Count));
        Assert.False(Directory.Exists(probes));
        Assert.Equal(0, new SegmentStore(_settings, Time).Count);
        Assert.Equal(1, _streams.Clears);
        Assert.Single(_refresher.Refreshed);
        // Alpha's folder stays (it holds a user file), so Jellyfin keeps its item.
        Assert.Equal(new[] { betaItem }, _items.Removed);
    }

    [Fact]
    public async Task Purge_keeps_titles_whose_library_root_is_not_mounted()
    {
        var alpha = AddMovie("tt1", "Alpha");
        var moviesRoot = Path.Combine(_settings.Current.LibraryRoot, "Movies");
        var parked = moviesRoot + "-offline";
        Directory.Move(moviesRoot, parked);
        var show = new TitleState { StateId = "series/tt9", Kind = MediaKind.Series, StremioId = "tt9", Folder = Path.Combine("Shows", "S (2000)"), Catalogs = [] };
        Directory.CreateDirectory(Path.Combine(_settings.Current.LibraryRoot, "Shows"));
        _titles.Use(state =>
        {
            state.Upsert(show);
            state.Save();
        });

        var removed = await Create().PurgeAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.NotNull(_titles.Get(alpha.StateId));
        Assert.Null(_titles.Get(show.StateId));
        Assert.True(Directory.Exists(Path.Combine(parked, Path.GetFileName(alpha.Folder))));
    }

    [Fact]
    public async Task Purge_skips_titles_jellyfin_has_no_item_for()
    {
        var alpha = AddMovie("tt1", "Alpha");
        var beta = AddMovie("tt2", "Beta");
        var betaItem = Guid.NewGuid();
        _items.Titles[beta.StateId] = betaItem;

        var removed = await Create().PurgeAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.Equal(new[] { betaItem }, _items.Removed);
        Assert.False(Directory.Exists(Folder(alpha)));
    }

    [Fact]
    public async Task Purge_keeps_the_jellyfin_item_of_an_unmarked_folder()
    {
        var alpha = AddMovie("tt1", "Alpha");
        File.Delete(Path.Combine(Folder(alpha), ".currents"));
        _items.Titles[alpha.StateId] = Guid.NewGuid();

        await Create().PurgeAsync(new Progress<double>(), CancellationToken.None);

        Assert.Empty(_items.Removed);
        Assert.True(Directory.Exists(Folder(alpha)));
    }

    [Fact]
    public async Task Purge_keeps_the_jellyfin_item_of_a_folder_kept_for_foreign_files()
    {
        var alpha = AddMovie("tt1", "Alpha");
        File.WriteAllText(Path.Combine(Folder(alpha), "notes.txt"), "mine");
        _items.Titles[alpha.StateId] = Guid.NewGuid();

        await Create().PurgeAsync(new Progress<double>(), CancellationToken.None);

        Assert.Empty(_items.Removed);
    }

    [Fact]
    public void Both_tasks_are_manual_with_stable_keys()
    {
        var maintenance = Create();
        var verify = new VerifyLibraryTask(maintenance);
        var purge = new PurgeContentTask(maintenance);

        Assert.Equal(Jellyfin.Plugin.Currents.Common.TaskKeys.VerifyLibrary, verify.Key);
        Assert.Equal(Jellyfin.Plugin.Currents.Common.TaskKeys.PurgeContent, purge.Key);
        Assert.Empty(verify.GetDefaultTriggers());
        Assert.Empty(purge.GetDefaultTriggers());
    }

    [Fact]
    public async Task Verify_waits_for_a_running_sync()
    {
        var sync = await _jobs.EnterAsync(CancellationToken.None);
        var verify = Create().VerifyAsync(new Progress<double>(), CancellationToken.None);

        await Task.Delay(100);
        Assert.False(verify.IsCompleted);

        sync.Dispose();
        await verify;
    }

    internal sealed class FakeDirectory : IUserDirectory
    {
        public List<DirectoryUser> Users { get; } = [];

        public IReadOnlyList<DirectoryUser> All() => Users;
    }

    internal sealed class CountingStreams : IStreamService
    {
        public int Clears { get; private set; }

        public Task<StreamLookup> GetAsync(StreamProfile profile, string type, string stremioId, TimeSpan wait, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public StreamLookup? Peek(StreamProfile profile, string type, string stremioId) => null;

        public void Clear() => Clears++;

        public StreamCacheStats Stats() => new(0, 0, 0);
    }
}
