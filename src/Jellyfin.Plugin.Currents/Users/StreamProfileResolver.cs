using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Users;

/// <summary>Applies spec §6 precedence: self-service (allowed and not locked) &gt; admin override &gt; global default &gt; none, field by field.</summary>
public sealed class StreamProfileResolver
{
    private readonly UserStore _users;
    private readonly ICurrentsSettings _settings;

    public StreamProfileResolver(UserStore users, ICurrentsSettings settings)
    {
        _users = users;
        _settings = settings;
    }

    /// <summary>Gets the effective profile.</summary>
    /// <param name="userId">The Jellyfin user, or null/empty for "no user" (background work, degraded .strm playback).</param>
    /// <returns>The profile.</returns>
    public StreamProfile For(Guid? userId)
    {
        var config = _settings.Current;
        var record = userId is { } id && id != Guid.Empty ? _users.Get(id) : null;
        var layers = new List<(ProfileSource Source, UserLayer Layer)>();
        if (record is not null)
        {
            if (config.AllowSelfService && !record.LockSelfService)
            {
                layers.Add((ProfileSource.Self, record.Self));
            }

            layers.Add((ProfileSource.Admin, record.Admin));
        }

        layers.Add((ProfileSource.Default, new UserLayer
        {
            AioStreamsManifestUrl = config.AioStreamsManifestUrl,
            Preferences = config.DefaultPreferences,
            AutoSelect = config.DefaultAutoSelect,
        }));

        var preferences = layers.Select(l => l.Layer.Preferences).FirstOrDefault(p => p is not null) ?? new StreamPreferences();
        preferences.Normalize();
        var autoSelect = layers.Select(l => l.Layer.AutoSelect).FirstOrDefault(a => a.HasValue) ?? false;

        if (record?.StreamsDisabled == true)
        {
            return new StreamProfile(ProfileSource.None, null, preferences, autoSelect, Disabled: true);
        }

        foreach (var (source, layer) in layers)
        {
            if (AioStreamsCredentials.TryParse(layer.AioStreamsManifestUrl, out var credentials, out _))
            {
                return new StreamProfile(source, credentials, preferences, autoSelect, Disabled: false);
            }
        }

        return new StreamProfile(ProfileSource.None, null, preferences, autoSelect, Disabled: false);
    }

    /// <summary>Gets whether this user sees AIOMetadata search results and may add them (spec §4.2, §6).</summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>True when search auto-add is on for the user.</returns>
    public bool SearchAutoAdd(Guid userId)
    {
        var config = _settings.Current;
        if (!config.EnableSearch || userId == Guid.Empty)
        {
            return false;
        }

        var record = _users.Get(userId);
        if (record.SearchAutoAddDisabled)
        {
            return false;
        }

        var self = config.AllowSelfService && !record.LockSelfService ? record.Self.SearchAutoAdd : null;
        return self ?? record.Admin.SearchAutoAdd ?? config.DefaultSearchAutoAdd;
    }
}
