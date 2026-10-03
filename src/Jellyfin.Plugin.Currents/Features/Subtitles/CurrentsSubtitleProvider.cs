using System.Diagnostics.CodeAnalysis;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Features.Subtitles;

/// <summary>
/// Jellyfin's subtitle search for Currents titles, answered by the AIOStreams subtitle addons of the requesting user's config
/// (the default config without a user). Automated searches (scans, the daily task) return nothing to spare the AIOStreams rate limit.
/// </summary>
public sealed class CurrentsSubtitleProvider : ISubtitleProvider
{
    private const int MaxResults = 25;
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);
    private readonly CurrentsItemLocator _locator;
    private readonly StreamProfileResolver _profiles;
    private readonly RequestContext _request;
    private readonly IAioStreamsClient _client;
    private readonly SubtitleDownloader _downloader;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<CurrentsSubtitleProvider> _logger;
    private readonly TtlCache<string, IReadOnlyList<StremioSubtitle>> _lists;

    public CurrentsSubtitleProvider(CurrentsItemLocator locator, StreamProfileResolver profiles, RequestContext request, IAioStreamsClient client, SubtitleDownloader downloader, ICurrentsSettings settings, TimeProvider time, ILogger<CurrentsSubtitleProvider> logger)
    {
        _locator = locator;
        _profiles = profiles;
        _request = request;
        _client = client;
        _downloader = downloader;
        _settings = settings;
        _logger = logger;
        _lists = new TtlCache<string, IReadOnlyList<StremioSubtitle>>(time);
    }

    public string Name => "Currents (AIOStreams)";

    public IEnumerable<VideoContentType> SupportedMediaTypes => [VideoContentType.Movie, VideoContentType.Episode];

    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        if (!_settings.Current.EnableSubtitles || request.IsAutomated || !_locator.TryGetTitle(request.MediaPath, out var title))
        {
            return [];
        }

        var wanted = LanguageCodes.ToIso6392(request.Language);
        var listed = await ListAsync(title, cancellationToken).ConfigureAwait(false);
        var matching = StreamSubtitles.Usable(
            listed.Where(s => wanted is null || LanguageCodes.ToIso6392(s.Lang) == wanted),
            perLanguage: MaxResults,
            total: MaxResults);
        return matching.Select((subtitle, i) =>
        {
            var language = LanguageCodes.ToIso6392(subtitle.Lang);
            return new RemoteSubtitleInfo
            {
                Id = SubtitleSearchId.Encode(title, StreamSubtitles.Key(subtitle.Url!)),
                ProviderName = Name,
                Name = $"AIOStreams {i + 1} ({language ?? subtitle.Lang})",
                ThreeLetterISOLanguageName = language ?? "und",
            };
        }).ToList();
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Jellyfin's SubtitleManager owns and disposes the response stream.")]
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        if (!_settings.Current.EnableSubtitles)
        {
            throw new InvalidOperationException("Currents subtitles are switched off.");
        }

        if (!SubtitleSearchId.TryDecode(id, out var title, out var key))
        {
            throw new ArgumentException("Unknown Currents subtitle id.", nameof(id));
        }

        var subtitle = (await ListAsync(title, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(s => s.Url is not null && StreamSubtitles.Key(s.Url) == key)
            ?? throw new InvalidOperationException("AIOStreams no longer offers this subtitle.");
        var text = await _downloader.DownloadAsync(new Uri(subtitle.Url!), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The subtitle could not be downloaded.");
        var format = SubtitleText.Sniff(text)
            ?? throw new InvalidOperationException("The subtitle is not SRT, WebVTT or ASS/SSA.");
        return new SubtitleResponse
        {
            Format = format,
            Language = LanguageCodes.ToIso6392(subtitle.Lang) ?? "und",
            IsForced = false,
            IsHearingImpaired = false,
            Stream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
        };
    }

    private async Task<IReadOnlyList<StremioSubtitle>> ListAsync(CurrentsTitle title, CancellationToken cancellationToken)
    {
        var userId = _request.UserId;
        var profile = _profiles.For(userId == Guid.Empty ? null : userId);
        if (!profile.CanPlay)
        {
            return [];
        }

        var key = $"{profile.Credentials!.Fingerprint()}/{title.Type}/{title.StremioId}";
        if (_lists.TryGet(key, out var cached))
        {
            return cached;
        }

        try
        {
            var subtitles = await _client.SubtitlesAsync(profile.Credentials, title.Type, title.StremioId, cancellationToken).ConfigureAwait(false);
            _lists.Set(key, subtitles, ListTtl);
            return subtitles;
        }
        catch (Exception ex) when (ex is AioStreamsException or HttpRequestException or CircuitOpenException or TimeoutException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _lists.Set(key, [], FailureTtl);
            _logger.LogWarning("AIOStreams subtitles for {Id} failed: {Reason}", title.StremioId, SecretMasker.Mask(ex.Message));
            return [];
        }
    }
}
