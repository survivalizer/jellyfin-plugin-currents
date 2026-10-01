#nullable disable
#pragma warning disable CA1031, CA1859, CA5351, CS1591, SA1600

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Spike;

/// <summary>SPIKE: returns two synthetic, never-persisted versions for items under /currents/spike/.</summary>
public sealed class SpikeMediaSourceManager : IMediaSourceManager
{
    private static readonly (string Name, string Url)[] Versions =
    [
        ("720p · synthetic", "http://media/sample-720.mp4"),
        ("1080p · synthetic", "http://media/sample-1080.mp4"),
    ];

    // SPIKE deviation: probe results per URL (MediaStreams etc.). Without MediaStreams the transcode
    // command line has no codec arguments and ffmpeg fails (see task-3-report.md).
    private static readonly ConcurrentDictionary<string, MediaSourceInfo> Probed = new(StringComparer.Ordinal);

    private readonly IMediaSourceManager _inner;
    private readonly ILogger<SpikeMediaSourceManager> _logger;

    public SpikeMediaSourceManager(IMediaSourceManager inner, ILogger<SpikeMediaSourceManager> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    private static bool IsSpike(BaseItem item) =>
        item?.Path?.Contains("/currents/spike/", StringComparison.Ordinal) == true;

    private static string VersionId(Guid itemId, int index) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{itemId:N}/{index}")).AsSpan(0, 16)).ToString("N");

    private IReadOnlyList<MediaSourceInfo> Synthetic(BaseItem item, string caller)
    {
        _logger.LogInformation("SPIKE {Caller} returning synthetic versions for {ItemId}", caller, item.Id);
        return Versions.Select((v, i) => new MediaSourceInfo
        {
            Id = VersionId(item.Id, i),
            Name = v.Name,
            Path = v.Url,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            Container = "mp4",
            Type = MediaSourceType.Default,
            SupportsDirectPlay = false,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            RunTimeTicks = TimeSpan.FromSeconds(60).Ticks,
        }).Select(ApplyProbe).ToList();
    }

    private static MediaSourceInfo ApplyProbe(MediaSourceInfo source)
    {
        if (Probed.TryGetValue(source.Path, out var p))
        {
            source.MediaStreams = p.MediaStreams;
            source.Bitrate = p.Bitrate;
            source.RunTimeTicks = p.RunTimeTicks ?? source.RunTimeTicks;
            source.Container = p.Container ?? source.Container;
            source.Size = p.Size;
        }

        return source;
    }

    private async Task<IReadOnlyList<MediaSourceInfo>> SyntheticProbedAsync(BaseItem item, string caller, CancellationToken cancellationToken)
    {
        var sources = Synthetic(item, caller);
        foreach (var source in sources.Where(s => !Probed.ContainsKey(s.Path)))
        {
            try
            {
                var probe = new MediaSourceInfo { Id = source.Id, Path = source.Path, Protocol = source.Protocol, IsRemote = true, Container = source.Container, RunTimeTicks = source.RunTimeTicks };
                await _inner.AddMediaInfoWithProbe(probe, false, null, false, false, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("SPIKE probed {Url}: {Count} streams, container {Container}, runtime {Ticks}", source.Path, probe.MediaStreams?.Count, probe.Container, probe.RunTimeTicks);
                Probed[source.Path] = probe;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SPIKE probe failed for {Url}", source.Path);
            }
        }

        return sources.Select(ApplyProbe).ToList();
    }

    public IReadOnlyList<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User user = null) =>
        IsSpike(item) ? Synthetic(item, nameof(GetStaticMediaSources)) : _inner.GetStaticMediaSources(item, enablePathSubstitution, user);

    public Task<IReadOnlyList<MediaSourceInfo>> GetPlaybackMediaSources(BaseItem item, User user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken) =>
        IsSpike(item)
            ? SyntheticProbedAsync(item, nameof(GetPlaybackMediaSources), cancellationToken)
            : _inner.GetPlaybackMediaSources(item, user, allowMediaProbe, enablePathSubstitution, cancellationToken);

    public async Task<MediaSourceInfo> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        if (!IsSpike(item))
        {
            return await _inner.GetMediaSource(item, mediaSourceId, liveStreamId, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
        }

        var sources = await SyntheticProbedAsync(item, nameof(GetMediaSource), cancellationToken).ConfigureAwait(false);
        return sources.FirstOrDefault(s => s.Id == mediaSourceId) ?? sources[0];
    }

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
}
