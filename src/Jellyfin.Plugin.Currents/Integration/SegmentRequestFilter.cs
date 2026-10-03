using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaSegments;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Backstop for clients that ignore HasSegments. GET /MediaSegments/{id} answers an empty list when the markers do not fit:
/// the version (a version id), the user's top version (an item id), or, with versions off, when the runtime cannot be known.
/// Runs before SyntheticVersionIdFilter rewrites the version id to the item id.
/// </summary>
public sealed class SegmentRequestFilter : IAsyncActionFilter
{
    private readonly VersionRegistry _registry;
    private readonly ILibraryManager _library;
    private readonly CurrentsItemLocator _locator;
    private readonly SegmentGate _gate;
    private readonly VersionSourceBuilder _builder;
    private readonly RequestContext _request;
    private readonly CompatState _compat;
    private readonly ICurrentsSettings _settings;

    public SegmentRequestFilter(VersionRegistry registry, ILibraryManager library, CurrentsItemLocator locator, SegmentGate gate, VersionSourceBuilder builder, RequestContext request, CompatState compat, ICurrentsSettings settings)
    {
        _registry = registry;
        _library = library;
        _locator = locator;
        _gate = gate;
        _builder = builder;
        _request = request;
        _compat = compat;
        _settings = settings;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "MediaSegments", ActionName: "GetItemSegments" }
            && context.ActionArguments.TryGetValue("itemId", out var raw) && raw is Guid id
            && Applies(id) == false)
        {
            context.Result = new OkObjectResult(new QueryResult<MediaSegmentDto>([]));
            return Task.CompletedTask;
        }

        return next();
    }

    // Null: not a Currents id, so the request is left alone.
    private bool? Applies(Guid id)
    {
        var versions = VersionsActive();
        if (_registry.TryGet(id.ToString("N"), out var version))
        {
            return _gate.Allows(version.Title, versions ? _builder.RealRunTimeTicks(version) : null);
        }

        if (!_locator.TryGetTitle(_library.GetItemById(id), out var title))
        {
            return null;
        }

        if (!versions)
        {
            return _gate.Allows(title, null);
        }

        var ranked = _registry.ForItemAndUser(id, _request.UserId);
        return _gate.Allows(title, ranked.Count == 0 ? null : _builder.RealRunTimeTicks(ranked[0]));
    }

    private bool VersionsActive() => _compat.Active && _settings.Current.EnableVersions;
}
