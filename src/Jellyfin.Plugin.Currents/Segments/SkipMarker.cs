namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>A skippable part of a title.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="StartMs">Where it starts, in milliseconds.</param>
/// <param name="EndMs">Where it ends, in milliseconds; null runs to the end of the file.</param>
public sealed record SkipMarker(MarkerKind Kind, long StartMs, long? EndMs);
