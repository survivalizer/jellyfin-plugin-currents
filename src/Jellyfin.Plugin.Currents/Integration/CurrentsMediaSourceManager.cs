using System.Diagnostics.CodeAnalysis;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Gives Currents items per-user synthetic versions (one per ranked AIOStreams stream); everything else goes to Jellyfin's own manager.</summary>
public sealed class CurrentsMediaSourceManager : IMediaSourceManager, IDisposable
{
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(10);
    private readonly IMediaSourceManager _inner;
    private readonly CurrentsItemLocator _locator;
    private readonly VersionCatalog _catalog;
    private readonly VersionRegistry _registry;
    private readonly VersionSourceBuilder _builder;
    private readonly TrackLocalizer _localizer;
    private readonly RequestContext _request;
    private readonly IInternalBaseUrl _internalUrl;
    private readonly SegmentGate _segmentGate;
    private readonly SegmentPresence _segmentPresence;
    private readonly CompatState _compat;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<CurrentsMediaSourceManager> _logger;

    public CurrentsMediaSourceManager(
        IMediaSourceManager inner,
        CurrentsItemLocator locator,
        VersionCatalog catalog,
        VersionRegistry registry,
        VersionSourceBuilder builder,
        TrackLocalizer localizer,
        RequestContext request,
        IInternalBaseUrl internalUrl,
        SegmentGate segmentGate,
        SegmentPresence segmentPresence,
        CompatState compat,
        ICurrentsSettings settings,
        ILogger<CurrentsMediaSourceManager> logger)
    {
        _inner = inner;
        _locator = locator;
        _catalog = catalog;
        _registry = registry;
        _builder = builder;
        _localizer = localizer;
        _request = request;
        _internalUrl = internalUrl;
        _segmentGate = segmentGate;
        _segmentPresence = segmentPresence;
        _compat = compat;
        _settings = settings;
        _logger = logger;
    }

    [SuppressMessage("Usage", "VSTHRD002", Justification = "Jellyfin's contract is synchronous; ASP.NET Core has no synchronization context and only single-item requests wait, bounded by SearchWait.")]
    public IReadOnlyList<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User? user = null)
    {
        if (!TryGetTitle(item, out var title))
        {
            return _inner.GetStaticMediaSources(item, enablePathSubstitution, user);
        }

        user ??= _request.User;
        var userId = user?.Id ?? _request.UserId;
        var list = _request.IsSingleItemRequest
            ? _catalog.GetAsync(item.Id, title, userId, SearchWait, CancellationToken.None).GetAwaiter().GetResult()
            : _catalog.Peek(item.Id, title, userId);
        return Sources(item, list, enablePathSubstitution, user, forPlayback: false);
    }

    public async Task<IReadOnlyList<MediaSourceInfo>> GetPlaybackMediaSources(BaseItem item, User? user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        if (!TryGetTitle(item, out var title))
        {
            return await _inner.GetPlaybackMediaSources(item, user, allowMediaProbe, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
        }

        user ??= _request.User;
        var userId = user?.Id ?? _request.UserId;
        var list = userId != Guid.Empty || _request.IsSingleItemRequest
            ? await _catalog.GetAsync(item.Id, title, userId, SearchWait, cancellationToken).ConfigureAwait(false)
            : Registered(item.Id, _request.IsAnonymousRequest);

        // A playback resumed after the stream cache expired must not lose its version when a fresh search comes back empty.
        if (userId != Guid.Empty && list is { Versions.Count: 0 } && _registry.ForItemAndUser(item.Id, userId) is { Count: > 0 } registered)
        {
            list = new VersionList(registered, null);
        }

        return Sources(item, list, enablePathSubstitution, user, forPlayback: true);
    }

    public async Task<MediaSourceInfo?> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        if (!TryGetTitle(item, out _))
        {
            return await _inner.GetMediaSource(item, mediaSourceId, liveStreamId, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
        }

        var requester = _request.UserId;
        if (_registry.TryGet(mediaSourceId, out var entry) && entry.BaseItemId == item.Id)
        {
            // A user may only reach their own versions and an anonymous HTTP caller only default-config ones;
            // background callers with no request (sessions, timers) may look any up by id.
            var allowed = requester == Guid.Empty
                ? !_request.IsAnonymousRequest || entry.UserId == Guid.Empty
                : requester == entry.UserId;
            if (!allowed)
            {
                return null;
            }

            var source = _builder.Build(entry, Context(item, enablePathSubstitution, requester == Guid.Empty ? null : _request.User, forPlayback: true));
            source.HasSegments = _segmentPresence.HasSegments(item.Id) && SegmentsApply(entry);
            AddDownloaded(source, DownloadedSubtitles(item));
            _localizer.Apply(source);
            return source;
        }

        var sources = await GetPlaybackMediaSources(item, null!, false, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
        var match = sources.FirstOrDefault(s => string.Equals(s.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase));
        if (match is null && string.Equals(mediaSourceId, item.Id.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            match = sources.Count > 0 ? sources[0] : null;
        }

        return match;
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();

    public void AddParts(IEnumerable<IMediaSourceProvider> providers) => _inner.AddParts(providers);

    public IReadOnlyList<MediaStream> GetMediaStreams(Guid itemId) => _inner.GetMediaStreams(itemId);

    public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query) => _inner.GetMediaStreams(query);

    public IReadOnlyList<MediaAttachment> GetMediaAttachments(Guid itemId) => _inner.GetMediaAttachments(itemId);

    public IReadOnlyList<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query) => _inner.GetMediaAttachments(query);

    public Task<LiveStreamResponse> OpenLiveStream(LiveStreamRequest request, CancellationToken cancellationToken) => _inner.OpenLiveStream(request, cancellationToken);

    public Task<Tuple<LiveStreamResponse, IDirectStreamProvider>> OpenLiveStreamInternal(LiveStreamRequest request, CancellationToken cancellationToken) => _inner.OpenLiveStreamInternal(request, cancellationToken);

    public Task<MediaSourceInfo> GetLiveStream(string id, CancellationToken cancellationToken) => _inner.GetLiveStream(id, cancellationToken);

    public Task<Tuple<MediaSourceInfo, IDirectStreamProvider>> GetLiveStreamWithDirectStreamProvider(string id, CancellationToken cancellationToken) => _inner.GetLiveStreamWithDirectStreamProvider(id, cancellationToken);

    public ILiveStream GetLiveStreamInfo(string id) => _inner.GetLiveStreamInfo(id);

    public ILiveStream GetLiveStreamInfoByUniqueId(string uniqueId) => _inner.GetLiveStreamInfoByUniqueId(uniqueId);

    public Task<IReadOnlyList<MediaSourceInfo>> GetRecordingStreamMediaSources(ActiveRecordingInfo info, CancellationToken cancellationToken) => _inner.GetRecordingStreamMediaSources(info, cancellationToken);

    public Task CloseLiveStream(string id) => _inner.CloseLiveStream(id);

    public Task<MediaSourceInfo> GetLiveStreamMediaInfo(string id, CancellationToken cancellationToken) => _inner.GetLiveStreamMediaInfo(id, cancellationToken);

    public bool SupportsDirectStream(string path, MediaProtocol protocol) => _inner.SupportsDirectStream(path, protocol);

    public MediaProtocol GetPathProtocol(string path) => _inner.GetPathProtocol(path);

    public void SetDefaultAudioAndSubtitleStreamIndices(BaseItem item, MediaSourceInfo source, User user) => _inner.SetDefaultAudioAndSubtitleStreamIndices(item, source, user);

    public Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, string cacheKey, bool addProbeDelay, bool isLiveStream, CancellationToken cancellationToken) =>
        _inner.AddMediaInfoWithProbe(mediaSource, isAudio, cacheKey, addProbeDelay, isLiveStream, cancellationToken);

    // jellyfin-web fetches markers only when the playing source has HasSegments; the gate keeps them off versions of another length.
    private bool SegmentsApply(VersionEntry version) => _segmentGate.Allows(version.Title, _builder.RealRunTimeTicks(version));

    private bool TryGetTitle(BaseItem item, [NotNullWhen(true)] out CurrentsTitle? title)
    {
        title = null;
        return _compat.Active && _settings.Current.EnableVersions && _locator.TryGetTitle(item, out title);
    }

    // Background work (no request) gets the item's latest list; an anonymous HTTP caller only default-config versions.
    private VersionList? Registered(Guid itemId, bool anonymous)
    {
        var entries = anonymous ? _registry.ForItemAndUser(itemId, Guid.Empty) : _registry.ForItem(itemId, Guid.Empty);
        return entries.Count == 0 ? null : new VersionList(entries, null);
    }

    private VersionContext Context(BaseItem item, bool redact, User? user, bool forPlayback) => new(
        redact ? string.Empty : _internalUrl.Value,
        redact,
        item.RunTimeTicks,
        user?.HasPermission(PermissionKind.EnablePlaybackRemuxing) ?? true,
        user?.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding) ?? true,
        forPlayback);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One malformed stream must not take down the whole version list; it is skipped and logged.")]
    private MediaSourceInfo? TryBuild(VersionEntry version, VersionContext context)
    {
        try
        {
            return _builder.Build(version, context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Skipping version {VersionId} of item {ItemId}: {Error}: {Message}", version.VersionId, version.BaseItemId, ex.GetType().Name, SecretMasker.Mask(ex.Message));
            return null;
        }
    }

    // Subtitles Jellyfin downloaded for the item (saved next to the .strm) are stored under the item id; every version gets its own copies.
    private List<MediaStream> DownloadedSubtitles(BaseItem item) =>
        _inner.GetMediaStreams(new MediaStreamQuery { ItemId = item.Id, Type = MediaStreamType.Subtitle })
            .Where(s => s.IsExternal && s.Index is >= 0 and < TrackIndexes.Synthetic)
            .ToList();

    private static void AddDownloaded(MediaSourceInfo source, List<MediaStream> downloaded)
    {
        if (downloaded.Count == 0)
        {
            return;
        }

        source.MediaStreams =
        [
            .. source.MediaStreams,
            .. downloaded.Select(s =>
            {
                var copy = MediaStreamCopy.Of(s);
                copy.Index = TrackIndexes.DownloadedSubtitles + s.Index;
                copy.SupportsExternalStream = true;
                return copy;
            }),
        ];
    }

    private List<MediaSourceInfo> Sources(BaseItem item, VersionList? list, bool redact, User? user, bool forPlayback)
    {
        if (list is null)
        {
            return [VersionSourceBuilder.Pending(item.Id)];
        }

        if (list.Versions.Count == 0)
        {
            return [VersionSourceBuilder.Notice(item.Id, list.Notice ?? "No streams found for this title.")];
        }

        var context = Context(item, redact, user, forPlayback);
        var downloaded = DownloadedSubtitles(item);
        var stored = _settings.Current.EnableSegments && _segmentPresence.HasSegments(item.Id);
        var sources = new List<MediaSourceInfo>(list.Versions.Count);
        foreach (var version in list.Versions)
        {
            if (TryBuild(version, context) is { } source)
            {
                source.HasSegments = stored && SegmentsApply(version);
                AddDownloaded(source, downloaded);
                _localizer.Apply(source);
                sources.Add(source);
            }
        }

        if (sources.Count == 0)
        {
            return [VersionSourceBuilder.Notice(item.Id, "No playable streams for this title.")];
        }

        if (user is not null)
        {
            foreach (var source in sources)
            {
                _inner.SetDefaultAudioAndSubtitleStreamIndices(item, source, user);
            }
        }

        return sources;
    }
}
