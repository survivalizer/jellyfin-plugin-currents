using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Facts about the current HTTP request, read defensively (background work may run with no request or a finished one).</summary>
public sealed class RequestContext
{
    private static readonly HashSet<(string Controller, string Action)> SingleItemActions =
    [
        ("UserLibrary", "GetItem"),
        ("UserLibrary", "GetItemLegacy"),
        ("MediaInfo", "GetPostedPlaybackInfo"),
        ("MediaInfo", "GetPlaybackInfo"),
    ];

    private readonly IHttpContextAccessor _http;
    private readonly IUserManager _users;

    public RequestContext(IHttpContextAccessor http, IUserManager users)
    {
        _http = http;
        _users = users;
    }

    /// <summary>Gets the requesting user, or <see cref="Guid.Empty"/>. <c>?userId=</c> is honoured only for API keys, as Jellyfin's own endpoints do.</summary>
    public Guid UserId
    {
        get
        {
            var context = Context();
            if (context is null)
            {
                return Guid.Empty;
            }

            var id = JellyfinClaims.GetUserId(context.User);
            if (id != Guid.Empty || !JellyfinClaims.IsApiKey(context.User))
            {
                return id;
            }

            return Guid.TryParse(context.Request.Query["userId"].FirstOrDefault(), out var queried) ? queried : Guid.Empty;
        }
    }

    /// <summary>Gets a value indicating whether this is an HTTP request with no user and no API key. Background work (no request) is not anonymous.</summary>
    public bool IsAnonymousRequest =>
        Context() is { } context && UserId == Guid.Empty && !JellyfinClaims.IsApiKey(context.User);

    /// <summary>
    /// Gets whether this caller may reach a version owned by <paramref name="ownerId"/> (<see cref="Guid.Empty"/> = the
    /// default config): a user only their own; background work and API keys any; an anonymous HTTP caller only
    /// default-config ones. The one rule the media source decorator and the subtitle filter share.
    /// </summary>
    /// <param name="ownerId">The version's user id.</param>
    /// <returns>True when the caller may reach it.</returns>
    public bool MayReach(Guid ownerId) =>
        UserId is var requester && requester == Guid.Empty
            ? !IsAnonymousRequest || ownerId == Guid.Empty
            : requester == ownerId;

    public User? User => UserId is var id && id != Guid.Empty ? _users.GetUserById(id) : null;

    /// <summary>Gets a value indicating whether this request is an item detail or PlaybackInfo call, the only places a cold AIOStreams search may run.</summary>
    public bool IsSingleItemRequest
    {
        get
        {
            var action = Context()?.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            return action is not null && SingleItemActions.Contains((action.ControllerName, action.ActionName));
        }
    }

    private HttpContext? Context()
    {
        try
        {
            var context = _http.HttpContext;
            _ = context?.Request.Method; // throws ObjectDisposedException for a finished request
            return context;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
