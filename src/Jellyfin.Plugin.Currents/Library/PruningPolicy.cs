namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Decides when a title that left its catalogs may be removed (spec D4).</summary>
public static class PruningPolicy
{
    public static bool ShouldPrune(TitleState title, int threshold, bool playedByAnyone) =>
        !title.AddedBySearch && !playedByAnyone && title.MissCount >= Math.Max(1, threshold);
}
