using System.Collections.Concurrent;
using System.Diagnostics;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Before a version plays: probes it once if its parsed track info is not enough, and gives the base item a runtime (resume needs one).</summary>
public sealed class VersionProber
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(10);
    private readonly IMediaSourceManager _media;
    private readonly ILibraryManager _library;
    private readonly ProbeCache _probes;
    private readonly VersionSourceBuilder _builder;
    private readonly IInternalBaseUrl _internalUrl;
    private readonly ILogger<VersionProber> _logger;
    private readonly TtlCache<string, bool> _failures;
    private readonly ConcurrentDictionary<string, Lazy<Task<ProbedMedia?>>> _inFlight = new(StringComparer.Ordinal);

    public VersionProber(IMediaSourceManager media, ILibraryManager library, ProbeCache probes, VersionSourceBuilder builder, IInternalBaseUrl internalUrl, TimeProvider time, ILogger<VersionProber> logger)
    {
        _media = media;
        _library = library;
        _probes = probes;
        _builder = builder;
        _internalUrl = internalUrl;
        _logger = logger;
        _failures = new TtlCache<string, bool>(time);
    }

    public async Task PrepareAsync(BaseItem item, VersionEntry entry, CancellationToken cancellationToken)
    {
        var result = entry.Stream.Result;
        var prefill = MediaStreamMapper.Prefill(result, item.RunTimeTicks);
        if (!_probes.TryGet(entry.Stream.Key, out var probed) && prefill.NeedsProbe && !_failures.TryGet(entry.Stream.Key, out _))
        {
            probed = await ProbeOnceAsync(entry, prefill.Container).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var runtime = probed?.RunTimeTicks ?? MediaStreamMapper.RunTimeTicks(result);
        if (item.RunTimeTicks is null or <= 0 && runtime is > 0)
        {
            item.RunTimeTicks = runtime;
            try
            {
                await _library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best-effort: playback must not fail because the runtime could not be saved. The in-memory value still helps this request.
                _logger.LogWarning("Could not save the runtime of {Id}: {Reason}", entry.Title.StremioId, SecretMasker.Mask(ex.Message));
            }
        }
    }

    private Task<ProbedMedia?> ProbeOnceAsync(VersionEntry entry, string container)
    {
        var key = entry.Stream.Key;
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<ProbedMedia?>>(() => ProbeAsync(entry, container)));
        var probe = lazy.Value;
        _ = probe.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<ProbedMedia?>>>(key, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return probe;
    }

    // Shared by concurrent PlaybackInfo calls, so it has its own timeout rather than any caller's token.
    private async Task<ProbedMedia?> ProbeAsync(VersionEntry entry, string container)
    {
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        var source = new MediaSourceInfo
        {
            Id = entry.VersionId,
            Path = _builder.PlaybackUrl(entry, _internalUrl.Value),
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            Container = container,
            AnalyzeDurationMs = 5000,
        };
        var started = Stopwatch.GetTimestamp();
        try
        {
            // cacheKey must be null: with a key Jellyfin throws on a cache miss (MediaSourceManager.cs:802-817). We cache ourselves.
            await _media.AddMediaInfoWithProbe(source, false, null!, false, false, timeout.Token).ConfigureAwait(false);
            if (source.MediaStreams is not { Count: > 0 })
            {
                _failures.Set(entry.Stream.Key, true, FailureTtl);
                _logger.LogWarning("Probing a version of {Id} found no tracks", entry.Title.StremioId);
                return null;
            }

            var probed = ProbedMedia.From(source);
            _probes.Set(entry.Stream.Key, probed);
            _logger.LogInformation("Probed a version of {Id} in {Seconds:0.0} s", entry.Title.StremioId, Stopwatch.GetElapsedTime(started).TotalSeconds);
            return probed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failures.Set(entry.Stream.Key, true, FailureTtl);
            _logger.LogWarning("Could not probe a version of {Id}: {Reason}", entry.Title.StremioId, SecretMasker.Mask(ex.Message));
            return null;
        }
    }
}
