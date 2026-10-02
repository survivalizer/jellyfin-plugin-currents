namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A user's versions of a title, or a notice explaining why there are none.</summary>
public sealed record VersionList(IReadOnlyList<VersionEntry> Versions, string? Notice)
{
    public static VersionList Unavailable(string notice) => new([], notice);
}
