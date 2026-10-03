namespace Jellyfin.Plugin.Currents.Library;

/// <summary>The collection one catalog should have after a sync.</summary>
/// <param name="CatalogKey">The catalog key ("{type}/{id}"), stored on the BoxSet as its CurrentsCatalog provider id.</param>
/// <param name="Name">The collection name.</param>
/// <param name="Titles">The catalog's titles, in catalog order.</param>
public sealed record CollectionPlan(string CatalogKey, string Name, IReadOnlyList<TitleState> Titles);
