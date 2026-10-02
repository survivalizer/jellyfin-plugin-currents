namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Outcome of one catalog sync run.</summary>
public sealed record SyncReport(int Written, int Unchanged, int Pruned, IReadOnlyList<string> FailedCatalogs);
