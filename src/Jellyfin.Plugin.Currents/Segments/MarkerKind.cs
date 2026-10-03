namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>The skip markers Currents knows. Features/Segments maps them to Jellyfin's MediaSegmentType.</summary>
public enum MarkerKind
{
    Intro,
    Recap,
    Outro,
    Preview,
}
