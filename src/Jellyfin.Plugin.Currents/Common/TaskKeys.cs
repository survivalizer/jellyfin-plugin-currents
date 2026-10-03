namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Keys of the Currents scheduled tasks (shown in Jellyfin's task list; also used to find a task to queue).</summary>
public static class TaskKeys
{
    public const string CatalogSync = "CurrentsCatalogSync";

    public const string SkipMarkers = "CurrentsSkipMarkers";
}
