using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>An AIOMetadata search catalog the admin can enable, with the library its results go to.</summary>
public sealed record SearchCatalogOption(string Type, string Id, string Name, CatalogTarget Target);
