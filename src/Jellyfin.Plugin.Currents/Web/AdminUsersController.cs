using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Admin per-user controls (spec §6): override config, lock self-service, disable streams or search auto-add.</summary>
[ApiController]
[Route("Currents/admin/users")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserDirectory _directory;
    private readonly UserStore _users;
    private readonly StreamProfileResolver _profiles;
    private readonly IAioStreamsClient _streams;

    public AdminUsersController(IUserDirectory directory, UserStore users, StreamProfileResolver profiles, IAioStreamsClient streams)
    {
        _directory = directory;
        _users = users;
        _profiles = profiles;
        _streams = streams;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<AdminUserRow>> List() =>
        Ok(_directory.All().OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(Row).ToList());

    [HttpPut("{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserRow>> Update([FromRoute] Guid userId, [FromBody] AdminUserUpdate update, CancellationToken cancellationToken)
    {
        var user = _directory.All().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        var url = update.AioStreamsManifestUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && await ManifestValidator.ValidateAsync(_streams, url, cancellationToken).ConfigureAwait(false) is { } error)
        {
            return BadRequest(new StatusMessage(SecretMasker.Mask(error.Message)));
        }

        update.Preferences?.Normalize();
        _users.Update(userId, record =>
        {
            UserSettingsController.Apply(record.Admin, url, update.Preferences, update.ClearPreferences, update.AutoSelect, update.ClearAutoSelect);
            record.LockSelfService = update.LockSelfService;
            record.StreamsDisabled = update.StreamsDisabled;
            record.SearchAutoAddDisabled = update.SearchAutoAddDisabled;
        });
        return Ok(Row(user));
    }

    private AdminUserRow Row(DirectoryUser user)
    {
        var record = _users.Get(user.Id);
        return new AdminUserRow(
            user.Id.ToString("N"),
            user.Name,
            _profiles.For(user.Id).Source.ToString(),
            ManifestValidator.HostOf(record.Admin.AioStreamsManifestUrl),
            ManifestValidator.HostOf(record.Self.AioStreamsManifestUrl),
            record.Admin.Preferences,
            record.Admin.AutoSelect,
            record.LockSelfService,
            record.StreamsDisabled,
            record.SearchAutoAddDisabled);
    }
}
