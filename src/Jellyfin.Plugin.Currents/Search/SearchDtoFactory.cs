using System.Security.Cryptography;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Search;

namespace Jellyfin.Plugin.Currents.Search;

/// <summary>Builds the search card for a remote result: what jellyfin-web needs to render and link it, and never the poster URL.</summary>
public static class SearchDtoFactory
{
    public static BaseItemDto Create(SearchResult result, string serverId)
    {
        var movie = result.Key.Kind == MediaKind.Movie;
        var dto = new BaseItemDto
        {
            Id = result.Id,
            ServerId = serverId,
            Name = result.Name,
            SortName = result.Name,
            ProductionYear = result.Year,
            Overview = result.Meta.Description,
            Type = movie ? BaseItemKind.Movie : BaseItemKind.Series,
            IsFolder = !movie,
            MediaType = movie ? MediaType.Video : MediaType.Unknown,
            LocationType = LocationType.Virtual,
            CanDelete = false,
            ProviderIds = ProviderIds(result.Key),
            ImageTags = [],
        };

        if (!string.IsNullOrWhiteSpace(result.Meta.Poster))
        {
            dto.ImageTags[ImageType.Primary] = PosterTag(result.Meta.Poster);
            dto.PrimaryImageAspectRatio = 2.0 / 3.0;
        }

        return dto;
    }

    /// <summary>The search hint for a remote result (GET /Search/Hints): the same identity and poster tag as the card, never the poster URL.</summary>
    /// <param name="result">The remote result.</param>
    /// <returns>The hint.</returns>
    public static SearchHint Hint(SearchResult result)
    {
        var movie = result.Key.Kind == MediaKind.Movie;
        var hint = new SearchHint
        {
            Id = result.Id,
            Name = result.Name,
            MatchedTerm = result.Name,
            ProductionYear = result.Year,
            Type = movie ? BaseItemKind.Movie : BaseItemKind.Series,
            IsFolder = !movie,
            MediaType = movie ? MediaType.Video : MediaType.Unknown,
        };
#pragma warning disable CS0618 // Jellyfin still fills the obsolete ItemId for older clients; so do we.
        hint.ItemId = result.Id;
#pragma warning restore CS0618

        if (!string.IsNullOrWhiteSpace(result.Meta.Poster))
        {
            hint.PrimaryImageTag = PosterTag(result.Meta.Poster);
            hint.PrimaryImageAspectRatio = 2.0 / 3.0;
        }

        return hint;
    }

    public static string PosterTag(string posterUrl) =>
        "currents" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(posterUrl)).AsSpan(0, 8));

    private static Dictionary<string, string> ProviderIds(TitleKey key)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal) { [CurrentsProviderIds.Currents] = key.StremioId };
        var name = key.Provider switch
        {
            "imdb" => nameof(MetadataProvider.Imdb),
            "tmdb" => nameof(MetadataProvider.Tmdb),
            "tvdb" => nameof(MetadataProvider.Tvdb),
            _ => null,
        };
        if (name is not null)
        {
            ids[name] = key.Value;
        }

        return ids;
    }
}
