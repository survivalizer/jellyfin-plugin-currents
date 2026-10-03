using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Features.Segments;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Features.Segments;

public sealed class CurrentsSegmentProviderTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly FakeSegmentSource _source = new("Fake", 2);
    private readonly Movie _movie;
    private readonly Movie _other;

    public CurrentsSegmentProviderTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        var strm = Path.Combine(_settings.Current.LibraryRoot, "Movies", "M (2020)", "M (2020).strm");
        Directory.CreateDirectory(Path.GetDirectoryName(strm)!);
        File.WriteAllText(strm, new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://192.168.1.5:8097", "movie", "tt1"));
        _movie = new Movie { Id = Guid.Parse("11111111111111111111111111111111"), Path = strm, Name = "M" };
        _other = new Movie { Id = Guid.Parse("22222222222222222222222222222222"), Path = Path.Combine(_settings.DataFolderPath, "elsewhere.mkv") };
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => (Guid)args[0]! == _movie.Id ? _movie : null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private CurrentsSegmentProvider Create()
    {
        var metadata = new FakeAioMetadataClient();
        metadata.Metas["movie/tt1"] = new StremioMeta { Id = "tt1", Runtime = "120 min" };
        var service = new SegmentService(
            [_source],
            new SegmentStore(_settings, _time),
            new MetaCache(metadata, _settings, _time, NullLogger<MetaCache>.Instance),
            new DiagnosticsLog(_time),
            NullLogger<SegmentService>.Instance);
        var services = new ServiceCollection().AddSingleton(_library.Instance).BuildServiceProvider();
        return new CurrentsSegmentProvider(services, new CurrentsItemLocator(_settings, _time), service, _settings);
    }

    private static MediaSegmentGenerationRequest Request(Guid itemId, IReadOnlyList<MediaSegmentDto>? existing = null) =>
        new() { ItemId = itemId, ExistingSegments = existing ?? [] };

    [Fact]
    public void Name_never_changes()
    {
        Assert.Equal("Currents", Create().Name);
        Assert.Equal("Currents", CurrentsSegmentProvider.ProviderName);
    }

    [Fact]
    public async Task Maps_markers_to_jellyfin_segments()
    {
        _source.Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 1_000, 5_000), new SkipMarker(MarkerKind.Outro, 7_000_000, null)], null);

        var segments = await Create().GetMediaSegments(Request(_movie.Id), CancellationToken.None);

        Assert.Collection(
            segments,
            s =>
            {
                Assert.Equal(_movie.Id, s.ItemId);
                Assert.Equal(MediaSegmentType.Intro, s.Type);
                Assert.Equal(TimeSpan.FromSeconds(1).Ticks, s.StartTicks);
                Assert.Equal(TimeSpan.FromSeconds(5).Ticks, s.EndTicks);
            },
            s =>
            {
                Assert.Equal(MediaSegmentType.Outro, s.Type);
                Assert.Equal(TimeSpan.FromSeconds(7000).Ticks, s.StartTicks);
                Assert.Equal(TimeSpan.FromMinutes(120).Ticks, s.EndTicks);
            });
    }

    [Fact]
    public async Task No_markers_is_an_empty_list()
    {
        Assert.Empty(await Create().GetMediaSegments(Request(_movie.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_source_failure_propagates_so_jellyfin_keeps_stored_segments()
    {
        _source.Error = new SegmentSourceException("down");

        await Assert.ThrowsAsync<SegmentSourceException>(() => Create().GetMediaSegments(Request(_movie.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_items_keep_their_existing_segments()
    {
        IReadOnlyList<MediaSegmentDto> existing = [new MediaSegmentDto { ItemId = _other.Id, Type = MediaSegmentType.Intro, StartTicks = 0, EndTicks = 10 }];

        Assert.Same(existing, await Create().GetMediaSegments(Request(_other.Id, existing), CancellationToken.None));
        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task Supports_only_currents_items_while_markers_are_on()
    {
        var provider = Create();

        Assert.True(await provider.Supports(_movie));
        Assert.False(await provider.Supports(_other));

        _settings.Current.EnableSegments = false;
        Assert.False(await provider.Supports(_movie));
    }

    [Theory]
    [InlineData(MarkerKind.Intro, MediaSegmentType.Intro)]
    [InlineData(MarkerKind.Recap, MediaSegmentType.Recap)]
    [InlineData(MarkerKind.Outro, MediaSegmentType.Outro)]
    [InlineData(MarkerKind.Preview, MediaSegmentType.Preview)]
    public void Kinds_map_to_jellyfin_types(MarkerKind kind, MediaSegmentType type) =>
        Assert.Equal(type, CurrentsSegmentProvider.Type(kind));
}
