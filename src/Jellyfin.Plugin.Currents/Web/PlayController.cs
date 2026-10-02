using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Target of every Currents .strm file. Anonymous because Jellyfin/ffmpeg fetch it server-side; protected by an HMAC signature.</summary>
[ApiController]
[Route("Currents")]
public sealed class PlayController : ControllerBase
{
    private readonly IStreamResolver _resolver;
    private readonly ICurrentsSettings _settings;

    /// <summary>Initializes a new instance of the <see cref="PlayController"/> class.</summary>
    /// <param name="resolver">The stream resolver.</param>
    /// <param name="settings">The plugin settings.</param>
    public PlayController(IStreamResolver resolver, ICurrentsSettings settings)
    {
        _resolver = resolver;
        _settings = settings;
    }

    /// <summary>Resolves a title to a playable stream and redirects to it.</summary>
    /// <param name="type">The Stremio type (movie or series).</param>
    /// <param name="id">The Stremio id.</param>
    /// <param name="sig">The HMAC signature.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A redirect to the stream, or an error status.</returns>
    [HttpGet("play/{type}/{id}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Play([FromRoute] string type, [FromRoute] string id, [FromQuery] string? sig, CancellationToken cancellationToken)
    {
        if (type is not ("movie" or "series"))
        {
            return NotFound();
        }

        if (!new StrmSigner(_settings.Current.SigningSecret).Verify(type, id, sig))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await _resolver.ResolveAsync(type, id, cancellationToken).ConfigureAwait(false);
        return result.Url is { } url
            ? Redirect(url.AbsoluteUri)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error);
    }
}
