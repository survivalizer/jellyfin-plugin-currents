using System.Text;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Serves a version's stream-attached subtitle as SRT. Only Jellyfin itself calls it (from Path); the signed token names the subtitle.</summary>
[ApiController]
[Route("Currents")]
public sealed class SubtitleProxyController : ControllerBase
{
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(30);
    private readonly IStreamService _streams;
    private readonly StreamProfileResolver _profiles;
    private readonly SubtitleDownloader _downloader;
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly LocalCallerPolicy _localCallers;

    public SubtitleProxyController(IStreamService streams, StreamProfileResolver profiles, SubtitleDownloader downloader, ICurrentsSettings settings, TimeProvider time, LocalCallerPolicy localCallers)
    {
        _streams = streams;
        _profiles = profiles;
        _downloader = downloader;
        _settings = settings;
        _time = time;
        _localCallers = localCallers;
    }

    /// <summary>Fetches the subtitle named by the token from upstream and returns it as SRT.</summary>
    /// <param name="file">"{token}.srt".</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The SRT file, or an error status.</returns>
    [HttpGet("subtitles/{file}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetSubtitle([FromRoute] string file, CancellationToken cancellationToken)
    {
        if (!ServerCaller.IsServer(HttpContext, _localCallers)
            || !file.EndsWith(".srt", StringComparison.Ordinal)
            || !new VersionTokenSigner(_settings.Current.SigningSecret, _time).TryReadSubtitle(file[..^4], out var ticket, out var key))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var profile = _profiles.For(ticket.UserId == Guid.Empty ? null : ticket.UserId);
        var lookup = await _streams.GetAsync(profile, ticket.Type, ticket.StremioId, SearchWait, cancellationToken).ConfigureAwait(false);
        var subtitle = lookup.Streams.FirstOrDefault(s => s.Key == ticket.StreamKey)?.Result.Subtitles?
            .FirstOrDefault(s => s.Url is not null && StreamSubtitles.Key(s.Url) == key);
        if (subtitle is null || !Uri.TryCreate(subtitle.Url, UriKind.Absolute, out var url))
        {
            return NotFound();
        }

        var text = await _downloader.DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        var srt = text is null ? null : SubtitleText.ToSrt(text);
        return srt is null
            ? StatusCode(StatusCodes.Status502BadGateway)
            : File(Encoding.UTF8.GetBytes(srt), "application/x-subrip; charset=utf-8");
    }
}
