using System.Globalization;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Configuration;
using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Endpoints used by the Currents admin page.</summary>
[ApiController]
[Route("Currents/admin")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class AdminController : ControllerBase
{
    private const string TestTitle = "tt0111161";
    private readonly IAioMetadataClient _metadata;
    private readonly IAioStreamsClient _streams;
    private readonly ICurrentsSettings _settings;
    private readonly ITaskManager _taskManager;

    public AdminController(IAioMetadataClient metadata, IAioStreamsClient streams, ICurrentsSettings settings, ITaskManager taskManager)
    {
        _metadata = metadata;
        _streams = streams;
        _settings = settings;
        _taskManager = taskManager;
    }

    [HttpPost("catalogs")]
    public async Task<ActionResult<IReadOnlyList<CatalogOption>>> GetCatalogs([FromBody] ManifestUrlRequest? request, CancellationToken cancellationToken)
    {
        if (!AioMetadataEndpoint.TryParse(request?.ManifestUrl, out var endpoint, out var error))
        {
            return BadRequest(new StatusMessage(SecretMasker.Mask(error!)));
        }

        try
        {
            var manifest = await _metadata.GetManifestAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var options = manifest.Catalogs
                .Where(c => !c.RequiresExtra && !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.Type))
                .Select(c => new CatalogOption(c.Type, c.Id, string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name))
                .ToList();
            return Ok(options);
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new StatusMessage(SecretMasker.Mask(ex.Message)));
        }
    }

    [HttpPost("search-catalogs")]
    public async Task<ActionResult<IReadOnlyList<SearchCatalogOption>>> GetSearchCatalogs([FromBody] ManifestUrlRequest? request, CancellationToken cancellationToken)
    {
        if (!AioMetadataEndpoint.TryParse(request?.ManifestUrl, out var endpoint, out var error))
        {
            return BadRequest(new StatusMessage(SecretMasker.Mask(error!)));
        }

        try
        {
            var manifest = await _metadata.GetManifestAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var options = manifest.Catalogs
                .Where(c => !string.IsNullOrEmpty(c.Id) && c.Extra.Exists(e => e.IsRequired && string.Equals(e.Name, "search", StringComparison.Ordinal)))
                .Select(c => (Catalog: c, Target: SearchTarget(c.Type)))
                .Where(x => x.Target is not null)
                .Select(x => new SearchCatalogOption(x.Catalog.Type, x.Catalog.Id, string.IsNullOrWhiteSpace(x.Catalog.Name) ? x.Catalog.Id : x.Catalog.Name, x.Target!.Value))
                .ToList();
            return Ok(options);
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new StatusMessage(SecretMasker.Mask(ex.Message)));
        }
    }

    [HttpPost("test-streams")]
    public async Task<ActionResult<StatusMessage>> TestStreams([FromBody] ManifestUrlRequest? request, CancellationToken cancellationToken)
    {
        if (!AioStreamsCredentials.TryParse(request?.ManifestUrl, out var credentials, out var error))
        {
            return BadRequest(new StatusMessage(SecretMasker.Mask(error!)));
        }

        try
        {
            var outcome = await _streams.SearchAsync(credentials, "movie", TestTitle, cancellationToken).ConfigureAwait(false);
            return Ok(new StatusMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"Connected. {outcome.Results.Count} stream(s) found for a test title.")));
        }
        catch (Exception ex) when (ex is AioStreamsException or HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new StatusMessage(SecretMasker.Mask(ex.Message)));
        }
    }

    [HttpPost("sync")]
    public ActionResult SyncNow()
    {
        _taskManager.QueueScheduledTask<CatalogSyncTask>();
        return Accepted();
    }

    [HttpGet("paths")]
    public ActionResult<LibraryPathsResponse> GetPaths()
    {
        var paths = LibraryPaths.FromSettings(_settings);
        return new LibraryPathsResponse(paths.Movies, paths.Shows);
    }

    private static CatalogTarget? SearchTarget(string type) => type switch
    {
        "movie" or "anime.movie" => CatalogTarget.Movies,
        "series" => CatalogTarget.Shows,
        _ when type.StartsWith("anime.", StringComparison.Ordinal) => CatalogTarget.Shows,
        _ => null,
    };
}
