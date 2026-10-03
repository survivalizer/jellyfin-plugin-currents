using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Guards Jellyfin's subtitle routes for Currents versions. These routes answer anonymous callers, and for a Currents
/// item Jellyfin would then fail with a 500 on the empty placeholder source: an anonymous caller gets 404 instead.
/// A built-in subtitle of a version over the admin's size limit also gets 404, so Jellyfin never starts reading the
/// whole remote file to extract it (stream-attached 1000+ and downloaded 2000+ subtitles are unaffected).
/// </summary>
public sealed class SubtitleRequestFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "GetSubtitle", "GetSubtitleWithTicks", "GetSubtitlePlaylist" };

    private readonly ILibraryManager _library;
    private readonly CurrentsItemLocator _locator;
    private readonly VersionRegistry _registry;
    private readonly VersionSourceBuilder _builder;
    private readonly RequestContext _request;
    private readonly CompatState _compat;
    private readonly ICurrentsSettings _settings;

    public SubtitleRequestFilter(ILibraryManager library, CurrentsItemLocator locator, VersionRegistry registry, VersionSourceBuilder builder, RequestContext request, CompatState compat, ICurrentsSettings settings)
    {
        _library = library;
        _locator = locator;
        _registry = registry;
        _builder = builder;
        _request = request;
        _compat = compat;
        _settings = settings;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_compat.Active && _settings.Current.EnableVersions
            && context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "Subtitle" } action && Actions.Contains(action.ActionName)
            && Refuse(context.ActionArguments))
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        return next();
    }

    private static object? Arg(IDictionary<string, object?> args, string key) => args.TryGetValue(key, out var value) ? value : null;

    private bool Refuse(IDictionary<string, object?> args)
    {
        // GetSubtitle(WithTicks) prefer their obsolete query arguments over the route ones, as Jellyfin does; the playlist has only the plain names.
        var sourceId = (Arg(args, "mediaSourceId") ?? Arg(args, "routeMediaSourceId")) as string;
        var index = (Arg(args, "index") ?? Arg(args, "routeIndex")) as int?;
        var itemId = (Arg(args, "itemId") ?? Arg(args, "routeItemId")) as Guid?;

        var version = sourceId is not null && _registry.TryGet(sourceId, out var entry) ? entry : null;
        if (_request.IsAnonymousRequest && (version is not null || IsCurrentsItem(itemId)))
        {
            return true;
        }

        return version is not null && index is < TrackIndexes.StreamSubtitles && _builder.HidesBuiltInSubtitles(version);
    }

    private bool IsCurrentsItem(Guid? itemId) =>
        itemId is { } id && id != Guid.Empty
        && (_registry.TryGetBaseItemId(id, out _) || (_library.GetItemById(id) is { } item && _locator.TryGetTitle(item, out _)));
}
