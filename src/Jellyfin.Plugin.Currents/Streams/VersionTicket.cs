namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>What a version URL may play: one stream of one title, resolved with one user's config.</summary>
/// <param name="UserId">The Jellyfin user id, or <see cref="Guid.Empty"/> for the server default config.</param>
/// <param name="Type">The Stremio type (movie or series).</param>
/// <param name="StremioId">The Stremio id (episode ids include season and episode).</param>
/// <param name="StreamKey">The stream key from <see cref="StreamIdentity.Keys"/>.</param>
public sealed record VersionTicket(Guid UserId, string Type, string StremioId, string StreamKey);
