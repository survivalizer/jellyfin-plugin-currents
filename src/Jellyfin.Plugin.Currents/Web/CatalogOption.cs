namespace Jellyfin.Plugin.Currents.Web;

/// <summary>A catalog the admin can choose to sync.</summary>
public sealed record CatalogOption(string Type, string Id, string Name);
