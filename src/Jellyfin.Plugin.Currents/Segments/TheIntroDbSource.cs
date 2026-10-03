using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>TheIntroDB (api.theintrodb.org/v3): community markers keyed by TMDB, IMDb or TVDB ids. Anonymous use allows 500 lookups a day, 1000 with a key.</summary>
public sealed class TheIntroDbSource : ISegmentSource, IDisposable
{
    internal const string BaseUrl = "https://api.theintrodb.org/v3/media";
    private const int MaxBodyBytes = 256 * 1024;
    private static readonly TimeSpan DefaultPause = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxPause = TimeSpan.FromHours(24);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly SourcePacer _pacer = new(25, TimeSpan.FromSeconds(10));
    private long _pausedUntilTicks;

    public TheIntroDbSource(IHttpClientFactory httpClientFactory, ICurrentsSettings settings, TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _time = time;
    }

    public string Name => "TheIntroDB";

    public int Priority => 2;

    public bool AppliesTo(SegmentRequest request) =>
        request.Provider is "imdb" or "tmdb" or "tvdb"
        && (request.Kind == MediaKind.Movie || request is { Season: not null, Episode: not null });

    public async Task<SourceMarkers?> GetAsync(SegmentRequest request, long? targetRunTimeTicks, CancellationToken cancellationToken)
    {
        var pausedUntil = new DateTimeOffset(Interlocked.Read(ref _pausedUntilTicks), TimeSpan.Zero);
        if (_time.GetUtcNow() < pausedUntil)
        {
            throw new SegmentSourceException(string.Create(CultureInfo.InvariantCulture, $"TheIntroDB's request limit was reached; paused until {pausedUntil:u}."));
        }

        await _pacer.WaitAsync(Name, cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Get, Url(request, targetRunTimeTicks));
        var key = _settings.Current.TheIntroDbApiKey.Trim();
        if (key.Length > 0)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        var body = await SegmentHttp.GetAsync(_httpClientFactory, message, Name, MaxBodyBytes, Pause, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var answer = SegmentHttp.Parse<Answer>(body, Name);
        var markers = new List<SkipMarker>();
        Add(markers, MarkerKind.Intro, answer.Intro);
        Add(markers, MarkerKind.Recap, answer.Recap);
        Add(markers, MarkerKind.Outro, answer.Credits);
        Add(markers, MarkerKind.Preview, answer.Preview);
        return markers.Count == 0 ? null : new SourceMarkers(markers, null);
    }

    public void Dispose() => _pacer.Dispose();

    // Ids were validated by TitleKey (digits, or "tt" + digits), so they need no escaping.
    internal static Uri Url(SegmentRequest request, long? targetRunTimeTicks)
    {
        var id = request.Provider switch
        {
            "tmdb" => "tmdb_id=" + request.Id,
            "tvdb" => "tvdb_id=" + request.Id,
            _ => "imdb_id=" + request.Id,
        };
        var episode = request.Kind == MediaKind.Series
            ? string.Create(CultureInfo.InvariantCulture, $"&season={request.Season}&episode={request.Episode}")
            : string.Empty;
        var duration = targetRunTimeTicks is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"&duration_ms={targetRunTimeTicks.Value / TimeSpan.TicksPerMillisecond}")
            : string.Empty;
        return new Uri($"{BaseUrl}?{id}{episode}{duration}");
    }

    private static void Add(List<SkipMarker> markers, MarkerKind kind, List<Range>? ranges)
    {
        foreach (var range in ranges ?? [])
        {
            var start = Math.Max(0, range.StartMs ?? 0);
            if (range.EndMs is null || range.EndMs > start)
            {
                markers.Add(new SkipMarker(kind, start, range.EndMs));
            }
        }
    }

    private void Pause(RetryConditionHeaderValue? retryAfter)
    {
        var now = _time.GetUtcNow();
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : DefaultPause);
        delay = delay <= TimeSpan.Zero ? DefaultPause : delay > MaxPause ? MaxPause : delay;
        Interlocked.Exchange(ref _pausedUntilTicks, (now + delay).UtcTicks);
    }

    private sealed class Answer
    {
        public List<Range>? Intro { get; set; }

        public List<Range>? Recap { get; set; }

        public List<Range>? Credits { get; set; }

        public List<Range>? Preview { get; set; }
    }

    private sealed class Range
    {
        [JsonPropertyName("start_ms")]
        public long? StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public long? EndMs { get; set; }
    }
}
