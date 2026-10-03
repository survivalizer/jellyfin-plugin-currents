using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>The two views of one version's tracks.</summary>
/// <param name="Display">Every known track, for the details page: real indexes when probed, else synthetic 500–999.</param>
/// <param name="Playback">What playback and ffmpeg see: the probed tracks, or one video and one audio stub with Index -1.</param>
/// <param name="Container">The container (mkv, mp4, …).</param>
/// <param name="RunTimeTicks">The runtime, if known.</param>
/// <param name="Bitrate">The total bitrate in bits/s, if known.</param>
/// <param name="Size">The file size, if known.</param>
/// <param name="Origin">Where the tracks came from.</param>
/// <param name="NeedsProbe">True when playback should probe first (unknown codecs, several audio tracks, embedded subtitles, Dolby Vision guesses).</param>
public sealed record VersionTracks(IReadOnlyList<MediaStream> Display, IReadOnlyList<MediaStream> Playback, string Container, long? RunTimeTicks, int? Bitrate, long? Size, TrackOrigin Origin, bool NeedsProbe);
