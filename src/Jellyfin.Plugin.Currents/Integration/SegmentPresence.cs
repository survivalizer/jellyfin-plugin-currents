using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Controller.MediaSegments;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Asks Jellyfin whether an item has stored segments. Resolved lazily: IMediaSegmentManager is built together with the segment providers, and the decorator must not need it at construction.</summary>
public sealed class SegmentPresence
{
    private readonly IServiceProvider _services;

    public SegmentPresence(IServiceProvider services) => _services = services;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing segment lookup must never break the version list; the version just gets no markers.")]
    public bool HasSegments(Guid itemId)
    {
        try
        {
            return _services.GetService<IMediaSegmentManager>()?.HasSegments(itemId) ?? false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }
}
