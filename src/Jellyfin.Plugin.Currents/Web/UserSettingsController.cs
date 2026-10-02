using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Self-service settings for the signed-in user (spec §6). Every endpoint is scoped to the caller's own record.</summary>
[ApiController]
[Route("Currents/user")]
public sealed class UserSettingsController : ControllerBase
{
    private readonly UserStore _users;
    private readonly StreamProfileResolver _profiles;
    private readonly IAioStreamsClient _streams;
    private readonly ICurrentsSettings _settings;

    public UserSettingsController(UserStore users, StreamProfileResolver profiles, IAioStreamsClient streams, ICurrentsSettings settings)
    {
        _users = users;
        _profiles = profiles;
        _streams = streams;
        _settings = settings;
    }

    /// <summary>Serves the self-service page. Anonymous: a browser navigation carries no Jellyfin auth header; the page's API calls do.</summary>
    /// <returns>The HTML page.</returns>
    [HttpGet]
    [AllowAnonymous]
    [Produces("text/html")]
    public IActionResult Page()
    {
        var stream = typeof(UserSettingsController).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Currents.Web.userPage.html");
        return stream is null ? NotFound() : File(stream, "text/html; charset=utf-8");
    }

    [HttpGet("settings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<UserSettingsResponse> GetSettings()
    {
        var userId = JellyfinClaims.GetUserId(User);
        return userId == Guid.Empty ? StatusCode(StatusCodes.Status403Forbidden) : Ok(Describe(userId));
    }

    [HttpPut("settings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserSettingsResponse>> UpdateSettings([FromBody] UserSettingsUpdate update, CancellationToken cancellationToken)
    {
        var userId = JellyfinClaims.GetUserId(User);
        if (userId == Guid.Empty)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!CanEdit(_users.Get(userId)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new StatusMessage("Your Currents settings are managed by your server admin."));
        }

        var url = update.AioStreamsManifestUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && await ManifestValidator.ValidateAsync(_streams, url, cancellationToken).ConfigureAwait(false) is { } error)
        {
            return BadRequest(new StatusMessage(SecretMasker.Mask(error)));
        }

        update.Preferences?.Normalize();
        _users.Update(userId, record => Apply(record.Self, url, update.Preferences, update.ClearPreferences, update.AutoSelect, update.ClearAutoSelect));
        return Ok(Describe(userId));
    }

    internal static void Apply(UserLayer layer, string? url, StreamPreferences? preferences, bool clearPreferences, bool? autoSelect, bool clearAutoSelect)
    {
        if (url is not null)
        {
            layer.AioStreamsManifestUrl = url.Length == 0 ? null : url;
        }

        if (clearPreferences)
        {
            layer.Preferences = null;
        }
        else if (preferences is not null)
        {
            layer.Preferences = preferences;
        }

        if (clearAutoSelect)
        {
            layer.AutoSelect = null;
        }
        else if (autoSelect is not null)
        {
            layer.AutoSelect = autoSelect;
        }
    }

    private bool CanEdit(UserRecord record) => _settings.Current.AllowSelfService && !record.LockSelfService;

    private UserSettingsResponse Describe(Guid userId)
    {
        var record = _users.Get(userId);
        var profile = _profiles.For(userId);
        return new UserSettingsResponse(
            CanEdit(record),
            record.StreamsDisabled,
            profile.Source.ToString(),
            ManifestValidator.HostOf(record.Self.AioStreamsManifestUrl),
            record.Self.Preferences,
            record.Self.AutoSelect,
            profile.Preferences,
            profile.AutoSelect);
    }
}
