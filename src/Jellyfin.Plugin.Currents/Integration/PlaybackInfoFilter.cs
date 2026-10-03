using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>On PlaybackInfo for a Currents item: maps the requested MediaSourceId to one of the user's versions, probes that version if needed and maps synthetic track indexes (against the pre-probe display) to real ones, before Jellyfin builds the playback answer; afterwards scrubs loopback URLs from the response.</summary>
public sealed class PlaybackInfoFilter : IAsyncActionFilter
{
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(10);
    private readonly ILibraryManager _library;
    private readonly CurrentsItemLocator _locator;
    private readonly VersionCatalog _catalog;
    private readonly VersionRegistry _registry;
    private readonly VersionProber _prober;
    private readonly VersionSourceBuilder _builder;
    private readonly RequestContext _request;
    private readonly ICurrentsSettings _settings;

    public PlaybackInfoFilter(ILibraryManager library, CurrentsItemLocator locator, VersionCatalog catalog, VersionRegistry registry, VersionProber prober, VersionSourceBuilder builder, RequestContext request, ICurrentsSettings settings)
    {
        _library = library;
        _locator = locator;
        _catalog = catalog;
        _registry = registry;
        _prober = prober;
        _builder = builder;
        _request = request;
        _settings = settings;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_settings.Current.EnableVersions
            && context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "MediaInfo", ActionName: "GetPostedPlaybackInfo" or "GetPlaybackInfo" }
            && context.ActionArguments.TryGetValue("itemId", out var raw) && raw is Guid itemId
            && _library.GetItemById(itemId) is { } item
            && _locator.TryGetTitle(item, out var title))
        {
            await PrepareAsync(context, item, title, context.HttpContext.RequestAborted).ConfigureAwait(false);
            var executed = await next().ConfigureAwait(false);
            if (executed.Result is ObjectResult { Value: PlaybackInfoResponse response })
            {
                Scrub(item.Id, response);
            }

            return;
        }

        await next().ConfigureAwait(false);
    }

    private async Task PrepareAsync(ActionExecutingContext context, BaseItem item, CurrentsTitle title, CancellationToken cancellationToken)
    {
        var userId = _request.UserId;
        if (userId == Guid.Empty && context.ActionArguments.TryGetValue("userId", out var rawUser) && rawUser is Guid argumentUser)
        {
            userId = argumentUser;
        }

        var list = await _catalog.GetAsync(item.Id, title, userId, SearchWait, cancellationToken).ConfigureAwait(false);
        if (list.Versions.Count == 0)
        {
            return;
        }

        var dto = context.ActionArguments.TryGetValue("playbackInfoDto", out var body) ? body : null;
        var dtoMediaSourceId = dto?.GetType().GetProperty("MediaSourceId");
        var requested = (context.ActionArguments.TryGetValue("mediaSourceId", out var query) ? query as string : null)
            ?? dtoMediaSourceId?.GetValue(dto) as string;

        var chosen = requested is null ? list.Versions[0] : Choose(list, requested);
        if (requested is not null && !string.Equals(requested, chosen.VersionId, StringComparison.OrdinalIgnoreCase))
        {
            context.ActionArguments["mediaSourceId"] = chosen.VersionId;
            dtoMediaSourceId?.SetValue(dto, chosen.VersionId);
        }

        // The details page offers synthetic indexes for unprobed versions. Map a choice to the real track, or clear it so ffmpeg picks its default.
        var display = _builder.DisplayBeforeProbe(chosen, item.RunTimeTicks);
        var audio = ReadIndex(context, dto, "audioStreamIndex", "AudioStreamIndex");
        var subtitle = ReadIndex(context, dto, "subtitleStreamIndex", "SubtitleStreamIndex");
        var probe = (TrackIndexes.IsSynthetic(audio) && audio != DefaultAudio(display)) || TrackIndexes.IsSynthetic(subtitle);
        var probed = await _prober.PrepareAsync(item, chosen, probe, cancellationToken).ConfigureAwait(false);
        var real = probed?.Streams();
        if (TrackIndexes.IsSynthetic(audio))
        {
            WriteIndex(context, dto, "audioStreamIndex", "AudioStreamIndex", real is null ? null : TrackMatcher.Map(display, real, audio!.Value, MediaStreamType.Audio));
        }

        if (TrackIndexes.IsSynthetic(subtitle))
        {
            WriteIndex(context, dto, "subtitleStreamIndex", "SubtitleStreamIndex", real is null ? null : TrackMatcher.Map(display, real, subtitle!.Value, MediaStreamType.Subtitle));
        }
    }

    // Query arguments win over the body in GetPostedPlaybackInfo, so both are read and both are written.
    private static int? ReadIndex(ActionExecutingContext context, object? dto, string argument, string property) =>
        context.ActionArguments.TryGetValue(argument, out var raw) && raw is int value
            ? value
            : dto?.GetType().GetProperty(property)?.GetValue(dto) as int?;

    private static void WriteIndex(ActionExecutingContext context, object? dto, string argument, string property, int? value)
    {
        if (context.ActionArguments.ContainsKey(argument))
        {
            context.ActionArguments[argument] = value;
        }

        dto?.GetType().GetProperty(property)?.SetValue(dto, value);
    }

    // What ffmpeg plays when no audio index is given: the default audio track, else the first.
    private static int? DefaultAudio(IReadOnlyList<MediaStream> display) =>
        display.Where(s => s.Type == MediaStreamType.Audio).OrderByDescending(s => s.IsDefault).Select(s => (int?)s.Index).FirstOrDefault();

    // Defense in depth: no loopback URL may reach a client, even if Jellyfin hands an external track's Path out as its delivery URL.
    private static void Scrub(Guid itemId, PlaybackInfoResponse response)
    {
        foreach (var source in response.MediaSources ?? [])
        {
            if (IsLoopback(source.Path))
            {
                source.Path = $"currents://version/{source.Id}";
            }

            foreach (var stream in source.MediaStreams ?? [])
            {
                if (IsLoopback(stream.DeliveryUrl))
                {
                    stream.DeliveryUrl = $"/Videos/{itemId:N}/{source.Id}/Subtitles/{stream.Index}/0/Stream.srt";
                    stream.IsExternalUrl = false;
                }

                if (IsLoopback(stream.Path))
                {
                    stream.Path = $"currents://subtitle/{source.Id}/{stream.Index}";
                }
            }
        }
    }

    private static bool IsLoopback(string? url) =>
        url is not null
        && (url.Contains("/Currents/play/", StringComparison.OrdinalIgnoreCase) || url.Contains("/Currents/subtitles/", StringComparison.OrdinalIgnoreCase));

    private VersionEntry Choose(VersionList list, string requested)
    {
        var exact = list.Versions.FirstOrDefault(v => string.Equals(v.VersionId, requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // A stale id (the list was re-ranked or rebuilt since the page loaded): keep the same stream if it is still offered.
        if (_registry.TryGet(requested, out var previous)
            && list.Versions.FirstOrDefault(v => v.Stream.Key == previous.Stream.Key) is { } same)
        {
            return same;
        }

        return list.Versions[0];
    }
}
