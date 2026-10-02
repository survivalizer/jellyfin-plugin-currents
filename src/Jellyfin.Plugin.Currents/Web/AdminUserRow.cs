using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>One user on the admin page. Hosts only, never manifest URLs.</summary>
public sealed record AdminUserRow(
    string Id,
    string Name,
    string Source,
    string? OverrideHost,
    string? OwnConfigHost,
    StreamPreferences? OverridePreferences,
    bool? OverrideAutoSelect,
    bool LockSelfService,
    bool StreamsDisabled,
    bool SearchAutoAddDisabled);
