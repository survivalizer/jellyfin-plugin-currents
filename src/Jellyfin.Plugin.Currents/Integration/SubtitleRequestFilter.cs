using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Guards Jellyfin's subtitle routes for Currents versions: callers only reach subtitles of versions they may play
/// (<see cref="RequestContext.MayReach"/>), and anonymous callers never reach Currents subtitles. Jellyfin would
/// otherwise fail with a 500 on a placeholder source, on another user's version, or on an id that names no version of
/// the item; all get 404 instead. A built-in subtitle of a version over the admin's size limit also gets 404, so
/// Jellyfin never starts reading the whole remote file to extract it (stream-attached 1000+ and downloaded 2000+
/// subtitles are unaffected). A version id the registry forgot (restart, expiry) is looked up again first. The item id
/// used as the media source names no version, and Jellyfin's subtitle route cannot serve it, so it gets 404 too.
/// </summary>
public sealed class SubtitleRequestFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "GetSubtitle", "GetSubtitleWithTicks", "GetSubtitlePlaylist" };
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(10);

    private readonly ILibraryManager _library;
    private readonly CurrentsItemLocator _locator;
    private readonly VersionRegistry _registry;
    private readonly VersionCatalog _catalog;
    private readonly VersionSourceBuilder _builder;
    private readonly RequestContext _request;
    private readonly CompatState _compat;
    private readonly ICurrentsSettings _settings;

    public SubtitleRequestFilter(ILibraryManager library, CurrentsItemLocator locator, VersionRegistry registry, VersionCatalog catalog, VersionSourceBuilder builder, RequestContext request, CompatState compat, ICurrentsSettings settings)
    {
        _library = library;
        _locator = locator;
        _registry = registry;
        _catalog = catalog;
        _builder = builder;
        _request = request;
        _compat = compat;
        _settings = settings;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_compat.Active && _settings.Current.EnableVersions
            && context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "Subtitle" } action && Actions.Contains(action.ActionName)
            && await RefuseAsync(context.ActionArguments, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next().ConfigureAwait(false);
    }

    private static object? Arg(IDictionary<string, object?> args, string key) => args.TryGetValue(key, out var value) ? value : null;

    private async Task<bool> RefuseAsync(IDictionary<string, object?> args, CancellationToken cancellationToken)
    {
        // GetSubtitle(WithTicks) prefer their obsolete query arguments over the route ones, as Jellyfin does; the playlist has only the plain names.
        var sourceId = (Arg(args, "mediaSourceId") ?? Arg(args, "routeMediaSourceId")) as string;
        var index = (Arg(args, "index") ?? Arg(args, "routeIndex")) as int?;
        var itemId = (Arg(args, "itemId") ?? Arg(args, "routeItemId")) as Guid?;

        var item = itemId is { } id && id != Guid.Empty ? _library.GetItemById(id) : null;
        CurrentsTitle? title = null;
        var currentsItem = item is not null && _locator.TryGetTitle(item, out title);
        var version = sourceId is not null && _registry.TryGet(sourceId, out var entry) ? entry : null;
        if (version is null && !currentsItem)
        {
            return false;
        }

        if (_request.IsAnonymousRequest)
        {
            return true;
        }

        if (version is null && currentsItem)
        {
            version = await ResolveAsync(item!, title!, sourceId, cancellationToken).ConfigureAwait(false);
        }

        // An id that names no version of this item (a placeholder, another item's version, an unknown id) would fail inside Jellyfin.
        if (version is null || version.BaseItemId != itemId || !_request.MayReach(version.UserId))
        {
            return true;
        }

        return index is < TrackIndexes.StreamSubtitles && _builder.HidesBuiltInSubtitles(version);
    }

    // Resolves a source id the registry does not know: a signed-in user's forgotten version id is looked up again.
    // The item id names no version (Jellyfin's subtitle route resolves sources without the decorator's item-id
    // fallback), so it resolves to nothing and gets 404. Only a signed-in user may trigger a search; API-key and
    // background callers use only registered versions, which the caller already checked.
    private async Task<VersionEntry?> ResolveAsync(BaseItem item, CurrentsTitle title, string? sourceId, CancellationToken cancellationToken)
    {
        var userId = _request.UserId;
        if (userId == Guid.Empty || sourceId is null)
        {
            return null;
        }

        await _catalog.GetAsync(item.Id, title, userId, SearchWait, cancellationToken).ConfigureAwait(false);
        return _registry.TryGet(sourceId, out var found) ? found : null;
    }
}
