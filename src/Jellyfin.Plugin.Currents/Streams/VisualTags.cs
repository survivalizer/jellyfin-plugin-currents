namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Helpers for AIOStreams' visual tags (HDR10, HDR10+, DV, HLG, 10bit, 3D, …).</summary>
public static class VisualTags
{
    /// <summary>Returns whether the tag denotes HDR, Dolby Vision or HLG.</summary>
    /// <param name="tag">The visual tag.</param>
    /// <returns>True for HDR-class tags; false for null or empty.</returns>
    public static bool IsHdr(string? tag) =>
        !string.IsNullOrEmpty(tag)
        && (tag.Contains("HDR", StringComparison.OrdinalIgnoreCase)
        || tag.Contains("DV", StringComparison.OrdinalIgnoreCase)
        || tag.Contains("HLG", StringComparison.OrdinalIgnoreCase));
}
