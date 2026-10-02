using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>On PlaybackInfo for a Currents item: maps the requested MediaSourceId to one of the user's versions and probes that version if needed, before Jellyfin builds the playback answer.</summary>
public sealed class PlaybackInfoFilter : IAsyncActionFilter
{
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(10);
    private readonly ILibraryManager _library;
    private readonly CurrentsItemLocator _locator;
    private readonly VersionCatalog _catalog;
    private readonly VersionRegistry _registry;
    private readonly VersionProber _prober;
    private readonly RequestContext _request;
    private readonly ICurrentsSettings _settings;

    public PlaybackInfoFilter(ILibraryManager library, CurrentsItemLocator locator, VersionCatalog catalog, VersionRegistry registry, VersionProber prober, RequestContext request, ICurrentsSettings settings)
    {
        _library = library;
        _locator = locator;
        _catalog = catalog;
        _registry = registry;
        _prober = prober;
        _request = request;
        _settings = settings;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_settings.Current.EnableVersions
            && context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "MediaInfo", ActionName: "GetPostedPlaybackInfo" or "GetPlaybackInfo" }
            && context.ActionArguments.TryGetValue("itemId", out var raw) && raw is Guid itemId
            && _library.GetItemById(itemId) is { } item
            && _locator.TryGetTitle(item, out var title))
        {
            await PrepareAsync(context, item, title, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }

        await next().ConfigureAwait(false);
    }

    private async Task PrepareAsync(ActionExecutingContext context, BaseItem item, CurrentsTitle title, CancellationToken cancellationToken)
    {
        var userId = _request.UserId;
        if (userId == Guid.Empty && context.ActionArguments.TryGetValue("userId", out var rawUser) && rawUser is Guid argumentUser)
        {
            userId = argumentUser;
        }

        var list = await _catalog.GetAsync(item.Id, title, userId, SearchWait, cancellationToken).ConfigureAwait(false);
        if (list.Versions.Count == 0)
        {
            return;
        }

        var dto = context.ActionArguments.TryGetValue("playbackInfoDto", out var body) ? body : null;
        var dtoMediaSourceId = dto?.GetType().GetProperty("MediaSourceId");
        var requested = (context.ActionArguments.TryGetValue("mediaSourceId", out var query) ? query as string : null)
            ?? dtoMediaSourceId?.GetValue(dto) as string;

        var chosen = requested is null ? list.Versions[0] : Choose(list, requested);
        if (requested is not null && !string.Equals(requested, chosen.VersionId, StringComparison.OrdinalIgnoreCase))
        {
            context.ActionArguments["mediaSourceId"] = chosen.VersionId;
            dtoMediaSourceId?.SetValue(dto, chosen.VersionId);
        }

        await _prober.PrepareAsync(item, chosen, cancellationToken).ConfigureAwait(false);
    }

    private VersionEntry Choose(VersionList list, string requested)
    {
        var exact = list.Versions.FirstOrDefault(v => string.Equals(v.VersionId, requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // A stale id (the list was re-ranked or rebuilt since the page loaded): keep the same stream if it is still offered.
        if (_registry.TryGet(requested, out var previous)
            && list.Versions.FirstOrDefault(v => v.Stream.Key == previous.Stream.Key) is { } same)
        {
            return same;
        }

        return list.Versions[0];
    }
}
