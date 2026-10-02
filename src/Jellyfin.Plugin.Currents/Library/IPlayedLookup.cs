namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Answers whether any Jellyfin user has played (or started) a title.</summary>
public interface IPlayedLookup
{
    bool IsPlayedByAnyone(string absoluteFolder, MediaKind kind);
}
