namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Per-call inputs for building version sources.</summary>
/// <param name="InternalBaseUrl">Loopback base URL for ffmpeg (no trailing slash).</param>
/// <param name="RedactPath">True for client-facing responses (Jellyfin's enablePathSubstitution).</param>
/// <param name="ItemRunTimeTicks">The base item's runtime, the last-resort version runtime.</param>
/// <param name="AllowRemux">The user may stream-copy (EnablePlaybackRemuxing).</param>
/// <param name="AllowTranscode">The user may transcode (EnableVideoPlaybackTranscoding).</param>
/// <param name="ForPlayback">True for sources ffmpeg and playback use (probed tracks or Index -1 stubs); false for display (item DTOs).</param>
public sealed record VersionContext(string InternalBaseUrl, bool RedactPath, long? ItemRunTimeTicks, bool AllowRemux, bool AllowTranscode, bool ForPlayback = false);
