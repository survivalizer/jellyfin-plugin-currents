using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Users;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Turns a user's ranked streams for a title into registered versions.</summary>
public sealed class VersionCatalog
{
    private readonly IStreamService _streams;
    private readonly StreamProfileResolver _profiles;
    private readonly VersionRegistry _registry;
    private readonly ICurrentsSettings _settings;

    public VersionCatalog(IStreamService streams, StreamProfileResolver profiles, VersionRegistry registry, ICurrentsSettings settings)
    {
        _streams = streams;
        _profiles = profiles;
        _registry = registry;
        _settings = settings;
    }

    public async Task<VersionList> GetAsync(Guid itemId, CurrentsTitle title, Guid userId, TimeSpan wait, CancellationToken cancellationToken)
    {
        var profile = _profiles.For(userId);
        var lookup = await _streams.GetAsync(profile, title.Type, title.StremioId, wait, cancellationToken).ConfigureAwait(false);
        return Build(itemId, title, userId, profile, lookup);
    }

    public VersionList? Peek(Guid itemId, CurrentsTitle title, Guid userId)
    {
        var profile = _profiles.For(userId);
        var lookup = _streams.Peek(profile, title.Type, title.StremioId);
        return lookup is null ? null : Build(itemId, title, userId, profile, lookup);
    }

    private VersionList Build(Guid itemId, CurrentsTitle title, Guid userId, StreamProfile profile, StreamLookup lookup)
    {
        if (lookup.Error is not null || lookup.Streams.Count == 0)
        {
            return VersionList.Unavailable(lookup.Error ?? "No streams found for this title.");
        }

        var count = profile.AutoSelect ? 1 : Math.Max(1, _settings.Current.MaxVersions);
        var versions = lookup.Streams
            .Take(count)
            .Select(s => new VersionEntry(StreamIdentity.VersionId(itemId, userId, s.Key), itemId, userId, title, s))
            .ToList();
        _registry.Register(itemId, userId, versions);
        return new VersionList(versions, null);
    }
}
