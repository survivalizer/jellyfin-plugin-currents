using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>What the diagnostics panel shows. Never contains a key, config URL or token.</summary>
/// <param name="Compat">The Jellyfin version check.</param>
/// <param name="VersionsEnabled">The admin's "show each stream as a version" switch.</param>
/// <param name="SegmentsEnabled">The admin's skip-marker switch.</param>
/// <param name="TheIntroDbKeySet">A TheIntroDB key is saved.</param>
/// <param name="PublicMetaDbKeySet">A PublicMetaDB key is saved.</param>
/// <param name="StreamCache">The stream-list cache.</param>
/// <param name="SkipMarkers">The skip-marker cache.</param>
/// <param name="ProbesStored">Probe results stored on disk.</param>
/// <param name="RecentProblems">The last recorded problems, newest first.</param>
public sealed record DiagnosticsResponse(
    CompatInfo Compat,
    bool VersionsEnabled,
    bool SegmentsEnabled,
    bool TheIntroDbKeySet,
    bool PublicMetaDbKeySet,
    CacheInfo StreamCache,
    CacheInfo SkipMarkers,
    int ProbesStored,
    IReadOnlyList<DiagnosticEvent> RecentProblems);
