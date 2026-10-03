using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>A remote search planned for one request.</summary>
/// <param name="Query">The search term as the client sent it.</param>
/// <param name="Kinds">The kinds to search and show.</param>
/// <param name="Limit">The client's page size, if any.</param>
/// <param name="UserId">The requesting user.</param>
public sealed record SearchPlan(string Query, IReadOnlySet<MediaKind> Kinds, int? Limit, Guid UserId);
