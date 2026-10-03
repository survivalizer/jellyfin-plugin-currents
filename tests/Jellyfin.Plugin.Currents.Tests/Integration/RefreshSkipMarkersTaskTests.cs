using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class RefreshSkipMarkersTaskTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly (IMediaSegmentManager Instance, InterfaceFake Fake) _segments = InterfaceFake.Create<IMediaSegmentManager>();
    private readonly List<BaseItem> _items = [];

    public RefreshSkipMarkersTaskTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _items.Add(CurrentsMovie("A", "tt1"));
        _items.Add(new Movie { Id = Guid.NewGuid(), Path = Path.Combine(_settings.DataFolderPath, "own.mkv"), Name = "Own" });
        _items.Add(CurrentsMovie("B", "tt2"));
        _library.Fake.On(nameof(ILibraryManager.GetItemList), _ => (IReadOnlyList<BaseItem>)_items);
        _library.Fake.On(nameof(ILibraryManager.GetLibraryOptions), _ => new LibraryOptions());
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private Movie CurrentsMovie(string name, string id)
    {
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", name, name + ".strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://h", "movie", id));
        return new Movie { Id = Guid.NewGuid(), Path = strm, Name = name };
    }

    private RefreshSkipMarkersTask Create() =>
        new(_library.Instance, _segments.Instance, new CurrentsItemLocator(_settings, _time), _settings, NullLogger<RefreshSkipMarkersTask>.Instance);

    [Fact]
    public async Task Runs_the_segment_providers_for_currents_items_only()
    {
        await Create().ExecuteAsync(new Progress<double>(), CancellationToken.None);

        var calls = _segments.Fake.Calls(nameof(IMediaSegmentManager.RunSegmentPluginProviders));
        Assert.Equal(new[] { "A", "B" }, calls.Select(c => ((BaseItem)c[0]!).Name));
        Assert.All(calls, c => Assert.False((bool)c[2]!));
    }

    [Fact]
    public async Task One_failing_item_does_not_stop_the_run()
    {
        _segments.Fake.On(nameof(IMediaSegmentManager.RunSegmentPluginProviders), args =>
            ((BaseItem)args[0]!).Name == "A" ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask);

        await Create().ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, _segments.Fake.Calls(nameof(IMediaSegmentManager.RunSegmentPluginProviders)).Count);
    }

    [Fact]
    public async Task Does_nothing_while_markers_are_off()
    {
        _settings.Current.EnableSegments = false;

        await Create().ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.GetItemList)));
    }

    [Fact]
    public void Is_manual_with_a_stable_key()
    {
        var task = Create();

        Assert.Equal(TaskKeys.SkipMarkers, task.Key);
        Assert.Equal("Currents", task.Category);
        Assert.Empty(task.GetDefaultTriggers());
    }
}
