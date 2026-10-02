namespace Jellyfin.Plugin.Currents.Clients.Posters;

/// <summary>A fetched poster.</summary>
public sealed record PosterImage(byte[] Bytes, string ContentType);
