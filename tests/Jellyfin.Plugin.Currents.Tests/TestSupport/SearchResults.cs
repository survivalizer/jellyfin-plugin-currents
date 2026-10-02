using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal static class SearchResults
{
    /// <summary>A search result whose id is keyed with <see cref="FakeSettings.Secret"/>, as RemoteSearch would make it.</summary>
    public static SearchResult For(TitleKey key, StremioMeta meta, string catalogType) =>
        new(SearchItemId.For(FakeSettings.Secret, key.StateId), key, meta, catalogType);
}
