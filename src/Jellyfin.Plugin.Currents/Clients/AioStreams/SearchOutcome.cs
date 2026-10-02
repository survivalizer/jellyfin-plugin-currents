using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>Streams found for a title plus human-readable upstream errors.</summary>
/// <param name="Results">Streams returned by AIOStreams.</param>
/// <param name="Errors">Upstream addon errors, formatted for display.</param>
public sealed record SearchOutcome(IReadOnlyList<StreamResult> Results, IReadOnlyList<string> Errors);
