namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>What one source knows about a title.</summary>
/// <param name="Markers">The markers, in the source's order.</param>
/// <param name="ReferenceTicks">The runtime the markers were measured on, when the source says so (AniSkip only).</param>
public sealed record SourceMarkers(IReadOnlyList<SkipMarker> Markers, long? ReferenceTicks);
