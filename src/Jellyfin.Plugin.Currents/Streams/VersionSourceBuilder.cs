using Jellyfin.Plugin.Currents.Clients.RemuxDb;
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

    private readonly RemuxDbCache _remux;

    public VersionSourceBuilder(ICurrentsSettings settings, TimeProvider time, ProbeCache probes, RemuxDbCache remux)
    {
        _settings = settings;
        _time = time;
        _probes = probes;
        _remux = remux;
    }

    public static MediaSourceInfo Notice(Guid itemId, string message) => Unplayable(itemId, message, "currents://notice");

    public static MediaSourceInfo Pending(Guid itemId) => Unplayable(itemId, "Streams load when you open this title", "currents://pending");

    public string PlaybackUrl(VersionEntry entry, string internalBaseUrl)
    {
        var lifetime = TimeSpan.FromHours(Math.Max(1, _settings.Current.VersionTokenHours));
        var token = new VersionTokenSigner(_settings.Current.SigningSecret, _time).Create(entry.Ticket, lifetime);
        return $"{internalBaseUrl.TrimEnd('/')}/{VersionTokenSigner.PathFor(token)}";
    }

    /// <summary>The version's tracks from the best source available right now (no network calls).</summary>
    public VersionTracks Tracks(VersionEntry entry, long? itemRunTimeTicks)
    {
        _probes.TryGet(entry.Stream.Key, out var probed);
        var remux = probed is null ? _remux.Match(entry.Title, entry.Stream.Result) : null;
        return TrackComposer.Compose(entry.Stream.Result, itemRunTimeTicks, probed, remux);
    }

    public MediaSourceInfo Build(VersionEntry entry, VersionContext context)
    {
        var tracks = Tracks(entry, context.ItemRunTimeTicks);
        var streams = (context.ForPlayback ? tracks.Playback : tracks.Display).ToList();

        return new MediaSourceInfo
        {
            Id = entry.VersionId,
            Name = StreamLabel.For(entry.Stream.Result),
            Path = context.RedactPath ? $"currents://version/{entry.VersionId}" : PlaybackUrl(entry, context.InternalBaseUrl),
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            Type = MediaSourceType.Default,
            VideoType = VideoType.VideoFile,
            Container = tracks.Container,
            Size = tracks.Size,
            RunTimeTicks = tracks.RunTimeTicks,
            Bitrate = tracks.Bitrate,
            SupportsDirectPlay = false,
            SupportsDirectStream = context.AllowRemux,
            SupportsTranscoding = context.AllowTranscode,
            SupportsProbing = true,
            RequiresOpening = false,
            RequiresClosing = false,
            IsInfiniteStream = false,
            MediaStreams = streams,
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
