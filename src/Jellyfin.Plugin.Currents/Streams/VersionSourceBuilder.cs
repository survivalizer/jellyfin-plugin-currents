using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Builds the MediaSourceInfo Jellyfin sees for each version. Always streams through Jellyfin (never direct play), so clients never receive stream URLs.</summary>
public sealed class VersionSourceBuilder
{
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ProbeCache _probes;

    public VersionSourceBuilder(ICurrentsSettings settings, TimeProvider time, ProbeCache probes)
    {
        _settings = settings;
        _time = time;
        _probes = probes;
    }

    public static MediaSourceInfo Notice(Guid itemId, string message) => Unplayable(itemId, message, "currents://notice");

    public static MediaSourceInfo Pending(Guid itemId) => Unplayable(itemId, "Streams load when you open this title", "currents://pending");

    public string PlaybackUrl(VersionEntry entry, string internalBaseUrl)
    {
        var lifetime = TimeSpan.FromHours(Math.Max(1, _settings.Current.VersionTokenHours));
        var token = new VersionTokenSigner(_settings.Current.SigningSecret, _time).Create(entry.Ticket, lifetime);
        return $"{internalBaseUrl.TrimEnd('/')}/{VersionTokenSigner.PathFor(token)}";
    }

    public MediaSourceInfo Build(VersionEntry entry, VersionContext context)
    {
        var result = entry.Stream.Result;
        var prefill = MediaStreamMapper.Prefill(result, context.ItemRunTimeTicks);
        _probes.TryGet(entry.Stream.Key, out var probed);
        var probedStreams = probed?.Streams();
        var streams = probedStreams is { Count: > 0 } ? probedStreams : prefill.Streams;

        return new MediaSourceInfo
        {
            Id = entry.VersionId,
            Name = StreamLabel.For(result),
            Path = context.RedactPath ? $"currents://version/{entry.VersionId}" : PlaybackUrl(entry, context.InternalBaseUrl),
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            Type = MediaSourceType.Default,
            VideoType = VideoType.VideoFile,
            Container = probed?.Container ?? prefill.Container,
            Size = probed?.Size ?? result.Size,
            RunTimeTicks = probed?.RunTimeTicks ?? prefill.RunTimeTicks,
            Bitrate = probed?.Bitrate ?? prefill.Bitrate,
            SupportsDirectPlay = false,
            SupportsDirectStream = context.AllowRemux,
            SupportsTranscoding = context.AllowTranscode,
            SupportsProbing = true,
            RequiresOpening = false,
            RequiresClosing = false,
            IsInfiniteStream = false,
            MediaStreams = streams.ToList(),
            MediaAttachments = [],
            RequiredHttpHeaders = [],
            Formats = [],
        };
    }

    private static MediaSourceInfo Unplayable(Guid itemId, string name, string path) => new()
    {
        Id = itemId.ToString("N"),
        Name = name,
        Path = path,
        Protocol = MediaProtocol.Http,
        IsRemote = true,
        Type = MediaSourceType.Default,
        SupportsDirectPlay = false,
        SupportsDirectStream = false,
        SupportsTranscoding = false,
        MediaStreams = [],
        MediaAttachments = [],
        RequiredHttpHeaders = [],
        Formats = [],
    };
}
