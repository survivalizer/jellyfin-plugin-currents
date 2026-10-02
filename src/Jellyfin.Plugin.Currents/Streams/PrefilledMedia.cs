using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Track info guessed from a release name, before any probe.</summary>
/// <param name="Streams">One video and one audio stream, both with Index -1.</param>
/// <param name="Container">The container (mkv, mp4, …).</param>
/// <param name="RunTimeTicks">The runtime, if known.</param>
/// <param name="Bitrate">The total bitrate in bits/s, if known.</param>
/// <param name="NeedsProbe">True when playback should probe this stream first.</param>
public sealed record PrefilledMedia(IReadOnlyList<MediaStream> Streams, string Container, long? RunTimeTicks, int? Bitrate, bool NeedsProbe);
