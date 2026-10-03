namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>
/// Index ranges inside one version: 0–499 real ffprobe indexes; 500–999 synthetic display indexes of an unprobed version
/// (never seen by ffmpeg); 1000–1999 stream-attached subtitles; 2000+ subtitles Jellyfin downloaded for the item.
/// </summary>
public static class TrackIndexes
{
    public const int Synthetic = 500;
    public const int StreamSubtitles = 1000;
    public const int DownloadedSubtitles = 2000;

    public static bool IsSynthetic(int? index) => index is >= Synthetic and < StreamSubtitles;
}
