using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>AniSkip (api.aniskip.com/v2): community anime markers keyed by MAL id and episode. Kitsu, AniList and AniDB ids are mapped to MAL through ARM (arm.haglund.dev).</summary>
public sealed class AniSkipSource : ISegmentSource, IDisposable
{
    internal const string SkipTimesBase = "https://api.aniskip.com/v2/skip-times/";
    internal const string ArmBase = "https://arm.haglund.dev/api/v2/ids";
    private const double ReleaseTolerance = 0.1;
    private const double PlainPreferenceTolerance = 0.01;
    private const int MaxBodyBytes = 256 * 1024;
    private static readonly TimeSpan MappingTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan MissingMappingTtl = TimeSpan.FromDays(1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SourcePacer _pacer;
    private readonly SourcePacer _armPacer;
    private readonly TtlCache<string, Mapping> _malIds;

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The pacers are owned by the instance and disposed in Dispose.")]
    public AniSkipSource(IHttpClientFactory httpClientFactory, TimeProvider time)
        : this(httpClientFactory, time, new SourcePacer(5, TimeSpan.FromSeconds(1)), new SourcePacer(5, TimeSpan.FromSeconds(1)))
    {
    }

    // AniSkip and ARM are separate services with separate limits; one shared limiter halved AniSkip's throughput on cold lookups.
    internal AniSkipSource(IHttpClientFactory httpClientFactory, TimeProvider time, SourcePacer skipTimes, SourcePacer arm)
    {
        _httpClientFactory = httpClientFactory;
        _malIds = new TtlCache<string, Mapping>(time);
        _pacer = skipTimes;
        _armPacer = arm;
    }

    public string Name => "AniSkip";

    public int Priority => 1;

    public bool AppliesTo(SegmentRequest request) => request.IsAnime && request.Episode is > 0;

    public async Task<SourceMarkers?> GetAsync(SegmentRequest request, long? targetRunTimeTicks, CancellationToken cancellationToken)
    {
        var malId = await MalIdAsync(request, cancellationToken).ConfigureAwait(false);
        if (malId is null)
        {
            return null;
        }

        await _pacer.WaitAsync(Name, cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{SkipTimesBase}{malId.Value}/{request.Episode!.Value}?types=op&types=ed&types=recap&types=mixed-op&types=mixed-ed&episodeLength=0")));
        var body = await SegmentHttp.GetAsync(_httpClientFactory, message, Name, MaxBodyBytes, null, cancellationToken).ConfigureAwait(false);
        return body is null ? null : Choose(SegmentHttp.Parse<Answer>(body, Name).Results ?? [], targetRunTimeTicks);
    }

    public void Dispose()
    {
        _pacer.Dispose();
        _armPacer.Dispose();
    }

    private static SourceMarkers? Choose(List<Result> results, long? targetRunTimeTicks)
    {
        var usable = results.Where(r => r.Interval is not null && r.EpisodeLength > 0 && Kind(r.SkipType) is not null).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var anchor = targetRunTimeTicks is > 0
            ? targetRunTimeTicks.Value / (double)TimeSpan.TicksPerSecond
            : (usable.Find(r => r.SkipType == "op") ?? usable[0]).EpisodeLength;

        var markers = new List<SkipMarker>();
        double? reference = null;
        foreach (var kind in new[] { MarkerKind.Intro, MarkerKind.Recap, MarkerKind.Outro })
        {
            var candidates = usable
                .Where(r => Kind(r.SkipType) == kind && Math.Abs(r.EpisodeLength - anchor) <= anchor * ReleaseTolerance && r.Interval!.EndTime > r.Interval.StartTime)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            // A plain type within 1 % of the anchor beats a mixed one; otherwise the nearest release wins whatever its type.
            var pick = candidates
                .Where(r => !r.SkipType!.StartsWith("mixed-", StringComparison.Ordinal) && Math.Abs(r.EpisodeLength - anchor) <= anchor * PlainPreferenceTolerance)
                .OrderBy(r => Math.Abs(r.EpisodeLength - anchor))
                .FirstOrDefault()
                ?? candidates.OrderBy(r => Math.Abs(r.EpisodeLength - anchor)).First();

            markers.Add(new SkipMarker(kind, Milliseconds(pick.Interval!.StartTime), Milliseconds(pick.Interval.EndTime)));
            reference ??= pick.EpisodeLength;
        }

        return markers.Count == 0 ? null : new SourceMarkers(markers, (long)Math.Round(reference!.Value * TimeSpan.TicksPerSecond));
    }

    private static MarkerKind? Kind(string? skipType) => skipType switch
    {
        "op" or "mixed-op" => MarkerKind.Intro,
        "ed" or "mixed-ed" => MarkerKind.Outro,
        "recap" => MarkerKind.Recap,
        _ => null,
    };

    private static long Milliseconds(double seconds) => (long)Math.Round(Math.Max(0, seconds) * 1000);

    // Ids were validated by TitleKey (digits only), so they need no escaping.
    private async Task<int?> MalIdAsync(SegmentRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider == "mal")
        {
            return int.TryParse(request.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        }

        var key = request.Provider + ":" + request.Id;
        if (_malIds.TryGet(key, out var cached))
        {
            return cached.MalId;
        }

        await _armPacer.WaitAsync("ARM", cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri($"{ArmBase}?source={request.Provider}&id={request.Id}"));
        var body = await SegmentHttp.GetAsync(_httpClientFactory, message, "ARM", 64 * 1024, null, cancellationToken).ConfigureAwait(false);
        ArmIds? ids;
        try
        {
            ids = body is null ? null : JsonSerializer.Deserialize<ArmIds>(body, SegmentHttp.Options);
        }
        catch (JsonException ex)
        {
            throw new SegmentSourceException("ARM returned invalid JSON.", ex);
        }

        var malId = ids?.MyAnimeList;
        _malIds.Set(key, new Mapping(malId), malId is null ? MissingMappingTtl : MappingTtl);
        return malId;
    }

    private sealed record Mapping(int? MalId);

    private sealed class Answer
    {
        public List<Result>? Results { get; set; }
    }

    private sealed class Result
    {
        public Interval? Interval { get; set; }

        public string? SkipType { get; set; }

        public double EpisodeLength { get; set; }
    }

    private sealed class Interval
    {
        public double StartTime { get; set; }

        public double EndTime { get; set; }
    }

    private sealed class ArmIds
    {
        public int? MyAnimeList { get; set; }
    }
}
