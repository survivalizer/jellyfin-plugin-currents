using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Currents.Metadata;

/// <summary>Registers the "Currents" provider id so NFO uniqueids and the metadata editor recognise it.</summary>
public sealed class CurrentsExternalId : IExternalId
{
    public string ProviderName => "Currents";

    public string Key => CurrentsProviderIds.Currents;

    public ExternalIdMediaType? Type => null;

    public bool Supports(IHasProviderIds item) => item is Movie or Series or Episode;
}
