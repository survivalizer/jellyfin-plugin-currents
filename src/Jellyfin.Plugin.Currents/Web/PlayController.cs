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
    // A proxy in front of Jellyfin connects from loopback; ffmpeg never sends these. X-Original-For is where ASP.NET moves X-Forwarded-For.
    private static readonly string[] ForwardedHeaders = ["X-Forwarded-For", "X-Original-For", "Forwarded", "X-Real-IP"];
    private readonly IStreamResolver _resolver;
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly LocalCallerPolicy _localCallers;

    /// <summary>Initializes a new instance of the <see cref="PlayController"/> class.</summary>
    /// <param name="resolver">The stream resolver.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="time">The time provider.</param>
    /// <param name="localCallers">The policy deciding which callers are the server itself.</param>
    public PlayController(IStreamResolver resolver, ICurrentsSettings settings, TimeProvider time, LocalCallerPolicy localCallers)
    {
        _resolver = resolver;
        _settings = settings;
        _time = time;
        _localCallers = localCallers;
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

    /// <summary>Resolves one version (one stream, one user) and redirects to it. Only Jellyfin itself (ffmpeg/ffprobe) may call it; the expiring signed token names what may be played.</summary>
    /// <param name="token">The version token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A redirect to the stream, or an error status.</returns>
    [HttpGet("play/s/{token}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PlayVersion([FromRoute] string token, CancellationToken cancellationToken)
    {
        if (Array.Exists(ForwardedHeaders, Request.Headers.ContainsKey)
            || !_localCallers.IsLocal(HttpContext.Connection.RemoteIpAddress)
            || !new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryRead(token, out var ticket))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await _resolver.ResolveAsync(ticket, cancellationToken).ConfigureAwait(false);
        return result.Url is { } url
            ? Redirect(url.AbsoluteUri)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error);
    }
}
