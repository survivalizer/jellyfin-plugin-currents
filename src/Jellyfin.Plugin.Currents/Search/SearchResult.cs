using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Metadata;

namespace Jellyfin.Plugin.Currents.Search;

/// <summary>A title AIOMetadata search returned.</summary>
/// <param name="Id">The search id (<see cref="SearchItemId"/>, keyed with the install secret).</param>
/// <param name="Key">The canonical title key.</param>
/// <param name="Meta">The search meta (near-full; no episode list).</param>
/// <param name="CatalogType">The search catalog's Stremio type, the fallback type for a meta lookup.</param>
public sealed record SearchResult(Guid Id, TitleKey Key, StremioMeta Meta, string CatalogType)
{
    public string Name => Meta.Name ?? string.Empty;

    public int? Year => MetaMapper.ParseYear(Meta);
}
