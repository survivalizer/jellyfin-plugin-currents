using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class RequestContextTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    internal static DefaultHttpContext Http(Guid? userId = null, bool apiKey = false, string? query = null, (string Controller, string Action)? action = null)
    {
        var claims = new List<Claim>();
        if (userId is { } id)
        {
            claims.Add(new Claim(JellyfinClaims.UserIdClaim, id.ToString("N")));
        }

        if (apiKey)
        {
            claims.Add(new Claim(JellyfinClaims.UserIdClaim, Guid.Empty.ToString("N")));
            claims.Add(new Claim(JellyfinClaims.IsApiKeyClaim, "True"));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Count > 0 ? "Custom" : null)) };
        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        if (action is { } a)
        {
            var descriptor = new ControllerActionDescriptor { ControllerName = a.Controller, ActionName = a.Action };
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(descriptor), a.Action));
        }

        return context;
    }

    internal static RequestContext Create(HttpContext? context, Func<Guid, User?>? users = null)
    {
        var (userManager, fake) = InterfaceFake.Create<IUserManager>();
        fake.On(nameof(IUserManager.GetUserById), args => users?.Invoke((Guid)args[0]!));
        return new RequestContext(new HttpContextAccessor { HttpContext = context }, userManager);
    }

    [Fact]
    public void User_comes_from_the_token_claim()
    {
        Assert.Equal(Alice, Create(Http(Alice)).UserId);
    }

    [Fact]
    public void Query_user_is_honoured_only_for_api_keys()
    {
        Assert.Equal(Alice, Create(Http(apiKey: true, query: $"?userId={Alice:N}")).UserId);
        Assert.Equal(Guid.Empty, Create(Http(query: $"?userId={Alice:N}")).UserId);
    }

    [Fact]
    public void No_request_means_no_user()
    {
        var context = Create(null);

        Assert.Equal(Guid.Empty, context.UserId);
        Assert.Null(context.User);
        Assert.False(context.IsSingleItemRequest);
    }

    [Fact]
    public void Anonymous_means_a_request_with_no_user_and_no_api_key()
    {
        Assert.True(Create(Http()).IsAnonymousRequest);
        Assert.False(Create(null).IsAnonymousRequest);
        Assert.False(Create(Http(Alice)).IsAnonymousRequest);
        Assert.False(Create(Http(apiKey: true)).IsAnonymousRequest);
    }

    [Theory]
    [InlineData("UserLibrary", "GetItem", true)]
    [InlineData("UserLibrary", "GetItemLegacy", true)]
    [InlineData("MediaInfo", "GetPostedPlaybackInfo", true)]
    [InlineData("MediaInfo", "GetPlaybackInfo", true)]
    [InlineData("Items", "GetItems", false)]
    [InlineData("UserLibrary", "GetLatestMedia", false)]
    [InlineData("Videos", "GetVideoStream", false)]
    public void Only_single_item_endpoints_may_search(string controller, string action, bool expected) =>
        Assert.Equal(expected, Create(Http(Alice, action: (controller, action))).IsSingleItemRequest);
}
