using System.Net;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class AniSkipSourceTests : IDisposable
{
    private const string Mal21Episode1 = "https://api.aniskip.com/v2/skip-times/21/1?types=op&types=ed&types=recap&types=mixed-op&types=mixed-ed&episodeLength=0";

    // Live shape of MAL 21 episode 1, plus a second op from a longer release.
    private const string Mal21Answer = """
        {"found":true,"results":[
         {"interval":{"startTime":310.571,"endTime":400.571},"skipType":"op","skipId":"a","episodeLength":1443.984},
         {"interval":{"startTime":1396.006,"endTime":1434},"skipType":"ed","skipId":"b","episodeLength":1434.985},
         {"interval":{"startTime":132.773,"endTime":201.296},"skipType":"recap","skipId":"c","episodeLength":1444},
         {"interval":{"startTime":186.231,"endTime":276.231},"skipType":"mixed-op","skipId":"d","episodeLength":1439.98},
         {"interval":{"startTime":1401,"endTime":1435},"skipType":"mixed-ed","skipId":"e","episodeLength":1435.06},
         {"interval":{"startTime":100,"endTime":190},"skipType":"op","skipId":"f","episodeLength":1484.78}],
         "message":"Successfully found skip times","statusCode":200}
        """;

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly List<Uri> _sent = [];
    private readonly AniSkipSource _source;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public AniSkipSourceTests()
    {
        _source = new AniSkipSource(
            new FakeHttpClientFactory(new StubHttpHandler(r =>
            {
                _sent.Add(r.RequestUri!);
                return _respond(r);
            })),
            _time);
    }

    public void Dispose() => _source.Dispose();

    private static SegmentRequest Anime(string provider, string id, int episode) => new(MediaKind.Series, provider, id, null, episode);

    [Fact]
    public async Task Mal_ids_query_aniskip_directly_and_x()
    {
        _respond = _ => StubHttpHandler.Json(Mal21Answer);

        var result = await _source.GetAsync(Anime("mal", "21", 1), TimeSpan.FromMinutes(24).Ticks, CancellationToken.None);

        Assert.Equal(Mal21Episode1, Assert.Single(_sent).AbsoluteUri);
        Assert.Equal(
            new[]
            {
                new SkipMarker(MarkerKind.Intro, 186_231, 276_231),
                new SkipMarker(MarkerKind.Recap, 132_773, 201_296),
                new SkipMarker(MarkerKind.Outro, 1_396_006, 1_434_000),
            },
            result!.Markers);
        Assert.Equal((long)Math.Round(1439.98 * TimeSpan.TicksPerSecond), result.ReferenceTicks);
    }

    [Fact]
    public async Task A_plain_type_only_wins_over_a_mixed_one_when_it_is_nearly_as_close()
    {
        _respond = _ => StubHttpHandler.Json("""
            {"found":true,"results":[
             {"interval":{"startTime":1300,"endTime":1400},"skipType":"ed","skipId":"a","episodeLength":1320},
             {"interval":{"startTime":1310,"endTime":1410},"skipType":"mixed-ed","skipId":"b","episodeLength":1439.5}]}
            """);

        var result = await _source.GetAsync(Anime("mal", "21", 1), TimeSpan.FromMinutes(24).Ticks, CancellationToken.None);

        Assert.Equal(new[] { new SkipMarker(MarkerKind.Outro, 1_310_000, 1_410_000) }, result!.Markers);
    }

    [Fact]
    public async Task Kitsu_ids_are_mapped_through_arm_once()
    {
        _respond = r => r.RequestUri!.Host == "arm.haglund.dev"
            ? StubHttpHandler.Json("""{"anidb":23,"anilist":21,"kitsu":7442,"myanimelist":21}""")
            : StubHttpHandler.Json(Mal21Answer);

        await _source.GetAsync(Anime("kitsu", "7442", 1), null, CancellationToken.None);
        await _source.GetAsync(Anime("kitsu", "7442", 1), null, CancellationToken.None);

        Assert.Equal(
            new[] { "https://arm.haglund.dev/api/v2/ids?source=kitsu&id=7442", Mal21Episode1, Mal21Episode1 },
            _sent.Select(u => u.AbsoluteUri));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"kitsu":7442,"myanimelist":null}""")]
    public async Task Unmapped_ids_have_no_markers(string armAnswer)
    {
        _respond = _ => StubHttpHandler.Json(armAnswer);

        Assert.Null(await _source.GetAsync(Anime("anilist", "999999", 1), null, CancellationToken.None));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task Far_off_releases_are_dropped()
    {
        _respond = _ => StubHttpHandler.Json("""{"found":true,"results":[{"interval":{"startTime":10,"endTime":100},"skipType":"op","skipId":"a","episodeLength":1800}]}""");

        Assert.Null(await _source.GetAsync(Anime("mal", "21", 1), TimeSpan.FromMinutes(24).Ticks, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_target_the_first_opening_sets_the_release()
    {
        _respond = _ => StubHttpHandler.Json("""
            {"found":true,"results":[
             {"interval":{"startTime":1300,"endTime":1400},"skipType":"ed","skipId":"b","episodeLength":1500},
             {"interval":{"startTime":10,"endTime":100},"skipType":"op","skipId":"a","episodeLength":1420},
             {"interval":{"startTime":1300,"endTime":1390},"skipType":"ed","skipId":"c","episodeLength":1421}]}
            """);

        var result = await _source.GetAsync(Anime("mal", "21", 1), null, CancellationToken.None);

        Assert.Equal(new[] { new SkipMarker(MarkerKind.Intro, 10_000, 100_000), new SkipMarker(MarkerKind.Outro, 1_300_000, 1_390_000) }, result!.Markers);
        Assert.Equal(1420 * TimeSpan.TicksPerSecond, result.ReferenceTicks);
    }

    [Fact]
    public async Task Not_found_is_null_and_errors_throw()
    {
        Assert.Null(await _source.GetAsync(Anime("mal", "21", 1), null, CancellationToken.None));

        _respond = _ => StubHttpHandler.Json("{}", HttpStatusCode.BadGateway);
        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Anime("mal", "21", 1), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("kitsu", 1, true)]
    [InlineData("mal", 1, true)]
    [InlineData("anidb", 1, true)]
    [InlineData("mal", null, false)]
    [InlineData("imdb", 1, false)]
    public void Applies_to_anime_episodes(string provider, int? episode, bool expected) =>
        Assert.Equal(expected, _source.AppliesTo(new SegmentRequest(MediaKind.Series, provider, "1", null, episode)));
}
