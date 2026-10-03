using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>PublicMetaDB skips (publicmetadb.com/api/external/skips): per-contributor intro and credits rows keyed by TMDB id. Needs the admin's API key.</summary>
public sealed class PublicMetaDbSource : ISegmentSource, IDisposable
{
    internal const string BaseUrl = "https://publicmetadb.com/api/external/skips";
    private const long AgreementMs = 5000;
    private const int MaxBodyBytes = 512 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentsSettings _settings;
    private readonly SourcePacer _pacer = new(20, TimeSpan.FromSeconds(1));

    public PublicMetaDbSource(IHttpClientFactory httpClientFactory, ICurrentsSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public string Name => "PublicMetaDB";

    public int Priority => 0;

    public bool AppliesTo(SegmentRequest request) =>
        !string.IsNullOrWhiteSpace(_settings.Current.PublicMetaDbApiKey)
        && request.Provider == "tmdb"
        && (request.Kind == MediaKind.Movie || request is { Season: not null, Episode: not null });

    public async Task<SourceMarkers?> GetAsync(SegmentRequest request, long? targetRunTimeTicks, CancellationToken cancellationToken)
    {
        await _pacer.WaitAsync(Name, cancellationToken).ConfigureAwait(false);
        var query = request.Kind == MediaKind.Movie
            ? $"tmdb_id={request.Id}&media_type=movie"
            : string.Create(CultureInfo.InvariantCulture, $"tmdb_id={request.Id}&media_type=tv&season={request.Season}&episode={request.Episode}");
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri($"{BaseUrl}?{query}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Current.PublicMetaDbApiKey.Trim());
        var body = await SegmentHttp.GetAsync(_httpClientFactory, message, Name, MaxBodyBytes, null, cancellationToken).ConfigureAwait(false);
        if (body is null || Pick(SegmentHttp.Parse<Answer>(body, Name).Items ?? []) is not { } row)
        {
            return null;
        }

        var markers = new List<SkipMarker>();
        if (row.IntroStartMs is { } introStart && row.IntroEndMs is { } introEnd && introEnd > introStart)
        {
            markers.Add(new SkipMarker(MarkerKind.Intro, Math.Max(0, introStart), introEnd));
        }

        if (row.CreditsStartMs is { } creditsStart && (row.CreditsEndMs is null || row.CreditsEndMs > creditsStart))
        {
            markers.Add(new SkipMarker(MarkerKind.Outro, Math.Max(0, creditsStart), row.CreditsEndMs));
        }

        return markers.Count == 0 ? null : new SourceMarkers(markers, null);
    }

    public void Dispose() => _pacer.Dispose();

    private static Row? Pick(List<Row> rows)
    {
        var usable = rows.Where(r => r.IntroStartMs is not null || r.CreditsStartMs is not null).ToList();
        var streaming = usable.Where(r => !string.Equals(r.Source, "physical", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = streaming.Count > 0 ? streaming : usable;
        return pool
            .OrderByDescending(r => pool.Count(o => Agrees(r, o)))
            .ThenByDescending(r => r.Updated ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static bool Agrees(Row a, Row b) =>
        Near(a.IntroStartMs, b.IntroStartMs) && Near(a.IntroEndMs, b.IntroEndMs)
        && Near(a.CreditsStartMs, b.CreditsStartMs) && Near(a.CreditsEndMs, b.CreditsEndMs);

    private static bool Near(long? a, long? b) =>
        a is null || b is null ? a is null && b is null : Math.Abs(a.Value - b.Value) <= AgreementMs;

    private sealed class Answer
    {
        public List<Row>? Items { get; set; }
    }

    private sealed class Row
    {
        public string? Source { get; set; }

        [JsonPropertyName("intro_start_ms")]
        public long? IntroStartMs { get; set; }

        [JsonPropertyName("intro_end_ms")]
        public long? IntroEndMs { get; set; }

        [JsonPropertyName("credits_start_ms")]
        public long? CreditsStartMs { get; set; }

        [JsonPropertyName("credits_end_ms")]
        public long? CreditsEndMs { get; set; }

        public DateTimeOffset? Updated { get; set; }
    }
}
