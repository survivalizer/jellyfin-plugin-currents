using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Segments;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Currents.Features.Segments;

/// <summary>
/// Jellyfin's media-segment provider for Currents titles: skip markers from TheIntroDB, AniSkip and PublicMetaDB.
/// Jellyfin builds it before the plugin is initialised, so the constructor reads no settings and resolves no Jellyfin services.
/// </summary>
public sealed class CurrentsSegmentProvider : IMediaSegmentProvider
{
    /// <summary>Jellyfin keys stored segments by MD5 of this name: changing it orphans every stored marker.</summary>
    public const string ProviderName = "Currents";
    private readonly IServiceProvider _services;
    private readonly CurrentsItemLocator _locator;
    private readonly SegmentService _segments;
    private readonly ICurrentsSettings _settings;

    public CurrentsSegmentProvider(IServiceProvider services, CurrentsItemLocator locator, SegmentService segments, ICurrentsSettings settings)
    {
        _services = services;
        _locator = locator;
        _segments = segments;
        _settings = settings;
    }

    public string Name => ProviderName;

    public ValueTask<bool> Supports(BaseItem item) =>
        ValueTask.FromResult(_settings.Current.EnableSegments && _locator.TryGetTitle(item, out _));

    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
    {
        var item = _services.GetRequiredService<ILibraryManager>().GetItemById(request.ItemId);
        if (!_locator.TryGetTitle(item, out var title))
        {
            return request.ExistingSegments;
        }

        // A SegmentSourceException propagates on purpose: Jellyfin then keeps the segments it stored earlier.
        var lookup = await _segments.GetAsync(title, cancellationToken).ConfigureAwait(false);
        return lookup.Markers
            .Select(m => new MediaSegmentDto
            {
                ItemId = request.ItemId,
                Type = Type(m.Kind),
                StartTicks = m.StartMs * TimeSpan.TicksPerMillisecond,
                EndTicks = m.EndMs!.Value * TimeSpan.TicksPerMillisecond,
            })
            .ToList();
    }

    // Lookups are cached per title, not per item, so a rewritten .strm has nothing to clean up.
    public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken) => Task.CompletedTask;

    internal static MediaSegmentType Type(MarkerKind kind) => kind switch
    {
        MarkerKind.Intro => MediaSegmentType.Intro,
        MarkerKind.Recap => MediaSegmentType.Recap,
        MarkerKind.Outro => MediaSegmentType.Outro,
        _ => MediaSegmentType.Preview,
    };
}
