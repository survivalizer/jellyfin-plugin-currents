namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>One synthetic version: one stream of one title, offered to one user.</summary>
/// <param name="VersionId">The MediaSource id (lower-case GUID "N").</param>
/// <param name="BaseItemId">The library item the version belongs to.</param>
/// <param name="UserId">The user it was built for (<see cref="Guid.Empty"/> = default config).</param>
/// <param name="Title">The Currents title.</param>
/// <param name="Stream">The ranked stream.</param>
public sealed record VersionEntry(string VersionId, Guid BaseItemId, Guid UserId, CurrentsTitle Title, RankedStream Stream)
{
    public VersionTicket Ticket => new(UserId, Title.Type, Title.StremioId, Stream.Key);
}
