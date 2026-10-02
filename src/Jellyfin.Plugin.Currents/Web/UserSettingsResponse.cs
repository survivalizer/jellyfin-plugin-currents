using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>What the self-service page shows. Never contains the manifest URL.</summary>
/// <param name="CanEdit">Self-service is on and the user is not locked.</param>
/// <param name="StreamsDisabled">The admin disabled streams for this user.</param>
/// <param name="Source">Which layer supplies the user's AIOStreams config: None, Default, Admin or Self.</param>
/// <param name="ConfigHost">Host of the user's own saved config, if any.</param>
/// <param name="Preferences">The user's own saved preferences, if any.</param>
/// <param name="AutoSelect">The user's own auto-select choice, if any.</param>
/// <param name="EffectivePreferences">The preferences in force.</param>
/// <param name="EffectiveAutoSelect">The auto-select setting in force.</param>
public sealed record UserSettingsResponse(
    bool CanEdit,
    bool StreamsDisabled,
    string Source,
    string? ConfigHost,
    StreamPreferences? Preferences,
    bool? AutoSelect,
    StreamPreferences EffectivePreferences,
    bool EffectiveAutoSelect);
