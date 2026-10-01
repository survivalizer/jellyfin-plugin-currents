using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Read access to the current plugin configuration (indirection for testability).</summary>
public interface ICurrentsSettings
{
    PluginConfiguration Current { get; }

    string DataFolderPath { get; }
}
