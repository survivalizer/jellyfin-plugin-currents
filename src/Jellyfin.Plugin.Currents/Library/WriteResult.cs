namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Where a title was written (relative to the library root) and whether any file changed.</summary>
public sealed record WriteResult(string RelativeFolder, bool Changed);
