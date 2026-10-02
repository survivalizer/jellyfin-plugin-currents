namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>The address Jellyfin's own ffmpeg/ffprobe use to reach this server.</summary>
public interface IInternalBaseUrl
{
    /// <summary>Gets the base URL with Jellyfin's base path and no trailing slash.</summary>
    string Value { get; }
}
