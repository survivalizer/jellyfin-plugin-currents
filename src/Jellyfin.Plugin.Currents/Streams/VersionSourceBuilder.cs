using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Builds the MediaSourceInfo Jellyfin sees for each version. Always streams through Jellyfin (never direct play), so clients never receive stream URLs.</summary>
public sealed class VersionSourceBuilder
{
    private const long BytesPerGb = 1_000_000_000;

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

    private TimeSpan TokenLifetime => TimeSpan.FromHours(Math.Max(1, _settings.Current.VersionTokenHours));

    public static MediaSourceInfo Notice(Guid itemId, string message) => Unplayable(itemId, message, "currents://notice");

    public static MediaSourceInfo Pending(Guid itemId) => Unplayable(itemId, "Streams load when you open this title", "currents://pending");

    public string PlaybackUrl(VersionEntry entry, string internalBaseUrl)
    {
        var token = new VersionTokenSigner(_settings.Current.SigningSecret, _time).Create(entry.Ticket, TokenLifetime);
        return $"{internalBaseUrl.TrimEnd('/')}/{VersionTokenSigner.PathFor(token)}";
    }

    /// <summary>The version's tracks from the best source available right now (no network calls).</summary>
    public VersionTracks Tracks(VersionEntry entry, long? itemRunTimeTicks)
    {
        _probes.TryGet(entry.Stream.Key, out var probed);
        var remux = probed is null ? _remux.Match(entry.Title, entry.Stream.Result) : null;
        return TrackComposer.Compose(entry.Stream.Result, itemRunTimeTicks, probed, remux);
    }

    /// <summary>The version's own runtime from a probe, RemuxDB or AIOStreams; null when none knows it (never the item's runtime).</summary>
    /// <param name="entry">The version.</param>
    /// <returns>The runtime in ticks, or null.</returns>
    public long? RealRunTimeTicks(VersionEntry entry) => Tracks(entry, null).RunTimeTicks;

    /// <summary>Gets whether the version's built-in subtitles are hidden and refused: it is larger than the admin's limit, and Jellyfin would read the whole file to extract one.</summary>
    /// <param name="entry">The version.</param>
    /// <returns>True when over the limit.</returns>
    public bool HidesBuiltInSubtitles(VersionEntry entry) => OverSubtitleLimit(Tracks(entry, null).Size);

    /// <summary>The display tracks a details page showed before the version was probed: the same inputs as <see cref="Tracks"/> minus the probe. Synthetic indexes in a request refer to these.</summary>
    /// <param name="entry">The version.</param>
    /// <param name="itemRunTimeTicks">The item's runtime.</param>
    /// <returns>The display streams.</returns>
    public IReadOnlyList<MediaStream> DisplayBeforeProbe(VersionEntry entry, long? itemRunTimeTicks) =>
        TrackComposer.Compose(entry.Stream.Result, itemRunTimeTicks, null, _remux.Match(entry.Title, entry.Stream.Result)).Display;

    public MediaSourceInfo Build(VersionEntry entry, VersionContext context)
    {
        var tracks = Tracks(entry, context.ItemRunTimeTicks);
        var streams = (context.ForPlayback ? tracks.Playback : tracks.Display).ToList();
        if (OverSubtitleLimit(tracks.Size))
        {
            HideBuiltInTextSubtitles(streams, context.ForPlayback);
        }

        streams.AddRange(ExternalSubtitles(entry, context));

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

    public string SubtitleUrl(VersionEntry entry, string subtitleKey, string internalBaseUrl)
    {
        var token = new VersionTokenSigner(_settings.Current.SigningSecret, _time).CreateSubtitle(entry.Ticket, subtitleKey, TokenLifetime);
        return $"{internalBaseUrl.TrimEnd('/')}/{VersionTokenSigner.PathForSubtitle(token)}";
    }

    // Stream-attached subtitles become external SRT tracks. Jellyfin reads them server-side from the loopback route; clients only see a placeholder Path.
    private IEnumerable<MediaStream> ExternalSubtitles(VersionEntry entry, VersionContext context)
    {
        if (!_settings.Current.EnableSubtitles)
        {
            yield break;
        }

        var usable = StreamSubtitles.Usable(entry.Stream.Result.Subtitles);
        for (var i = 0; i < usable.Count; i++)
        {
            var index = TrackIndexes.StreamSubtitles + i;
            var language = LanguageCodes.ToIso6392(usable[i].Lang);
            yield return new MediaStream
            {
                Type = MediaStreamType.Subtitle,
                Index = index,
                Codec = "srt",
                Language = language,
                Title = language is null ? Label(usable[i].Lang) : null,
                IsExternal = true,
                SupportsExternalStream = true,
                Path = context.RedactPath
                    ? $"currents://subtitle/{entry.VersionId}/{index}"
                    : SubtitleUrl(entry, StreamSubtitles.Key(usable[i].Url!), context.InternalBaseUrl),
            };
        }
    }

    private bool OverSubtitleLimit(long? size) =>
        _settings.Current.EmbeddedSubtitleMaxGb is var limit && limit > 0 && size > limit * BytesPerGb;

    // Jellyfin extracts a built-in text subtitle by reading the whole remote file (SubtitleEncoder.ExtractAllExtractableSubtitles).
    // Display drops them all, and ones of unknown codec too. Playback may only drop a trailing run: ffmpeg maps embedded streams
    // by their position among streams sharing a Path (EncodingHelper.FindIndex), so removing one before an audio track would shift it.
    // Streams of type EmbeddedImage, Data and Lyric (cover art, timecodes, lyrics) are never mapped for playback, so the walk steps
    // over them and a removal before them shifts nothing ffmpeg uses. External streams are stepped over for the same reason.
    // Graphical subtitles stay: burning them in reads them from the same input without extraction.
    private static void HideBuiltInTextSubtitles(List<MediaStream> streams, bool forPlayback)
    {
        if (!forPlayback)
        {
            streams.RemoveAll(MayBeBuiltInText);
            return;
        }

        for (var i = streams.Count - 1; i >= 0; i--)
        {
            if (streams[i].IsExternal || streams[i].Type is MediaStreamType.EmbeddedImage or MediaStreamType.Data or MediaStreamType.Lyric)
            {
                continue;
            }

            if (!MayBeBuiltInText(streams[i]))
            {
                break;
            }

            streams.RemoveAt(i);
        }
    }

    private static bool MayBeBuiltInText(MediaStream stream) =>
        stream.Type == MediaStreamType.Subtitle && !stream.IsExternal && (string.IsNullOrEmpty(stream.Codec) || stream.IsTextSubtitleStream);

    // An unrecognised language label is shown as given, shortened.
    private static string? Label(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > 40 ? text[..40] : text;
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
