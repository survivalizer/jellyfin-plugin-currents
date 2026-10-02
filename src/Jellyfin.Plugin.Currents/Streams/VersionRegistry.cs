using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Process-wide map of synthetic version ids, for the Jellyfin paths that look versions up by id with no user (streaming, sessions, subtitles).</summary>
public sealed class VersionRegistry
{
    private readonly ICurrentsSettings _settings;
    private readonly TtlCache<string, VersionEntry> _byId;
    private readonly TtlCache<string, IReadOnlyList<VersionEntry>> _byItemAndUser;
    private readonly TtlCache<Guid, IReadOnlyList<VersionEntry>> _latestByItem;

    public VersionRegistry(ICurrentsSettings settings, TimeProvider time)
    {
        _settings = settings;
        _byId = new TtlCache<string, VersionEntry>(time);
        _byItemAndUser = new TtlCache<string, IReadOnlyList<VersionEntry>>(time);
        _latestByItem = new TtlCache<Guid, IReadOnlyList<VersionEntry>>(time);
    }

    private TimeSpan Lifetime => TimeSpan.FromHours(Math.Max(1, _settings.Current.VersionTokenHours));

    public void Register(Guid itemId, Guid userId, IReadOnlyList<VersionEntry> versions)
    {
        var lifetime = Lifetime;
        foreach (var version in versions)
        {
            _byId.Set(version.VersionId, version, lifetime);
        }

        _byItemAndUser.Set(Key(itemId, userId), versions, lifetime);
        _latestByItem.Set(itemId, versions, lifetime);
    }

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Version ids are lower-case GUID \"N\" strings.")]
    public bool TryGet(string versionId, [NotNullWhen(true)] out VersionEntry? entry) =>
        _byId.TryGet(versionId.ToLowerInvariant(), out entry);

    public bool TryGetBaseItemId(Guid versionId, out Guid baseItemId)
    {
        if (_byId.TryGet(versionId.ToString("N"), out var entry))
        {
            baseItemId = entry.BaseItemId;
            return true;
        }

        baseItemId = Guid.Empty;
        return false;
    }

    public IReadOnlyList<VersionEntry> ForItem(Guid itemId, Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return _latestByItem.TryGet(itemId, out var latest) ? latest : [];
        }

        return _byItemAndUser.TryGet(Key(itemId, userId), out var list) ? list : [];
    }

    private static string Key(Guid itemId, Guid userId) => $"{itemId:N}|{userId:N}";
}
