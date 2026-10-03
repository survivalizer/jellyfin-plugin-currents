namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>A title's merged, sanitised markers. Every marker has an end.</summary>
/// <param name="Markers">The markers, sorted by start.</param>
/// <param name="ReferenceTicks">The runtime the markers belong to, if known.</param>
public sealed record SegmentLookup(IReadOnlyList<SkipMarker> Markers, long? ReferenceTicks)
{
    public static SegmentLookup None { get; } = new([], null);
}
