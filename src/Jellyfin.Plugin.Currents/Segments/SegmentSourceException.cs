namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>A skip-marker source could not be asked. Jellyfin then keeps the markers it already stored.</summary>
public sealed class SegmentSourceException : Exception
{
    public SegmentSourceException()
    {
    }

    public SegmentSourceException(string message)
        : base(message)
    {
    }

    public SegmentSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
