namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>A user's versions of a title, or a notice explaining why there are none.</summary>
/// <param name="Versions">The versions, best first.</param>
/// <param name="Notice">Why there are none, if so.</param>
/// <param name="Refused">True when the user may not play at all (streams off or no config), so no earlier version may stand in.</param>
public sealed record VersionList(IReadOnlyList<VersionEntry> Versions, string? Notice, bool Refused = false)
{
    public static VersionList Unavailable(string notice) => new([], notice);

    public static VersionList Refusal(string notice) => new([], notice, Refused: true);
}
