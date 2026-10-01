using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Reads configuration from the loaded <see cref="CurrentsPlugin"/> instance.</summary>
public sealed class PluginSettings : ICurrentsSettings
{
    public PluginConfiguration Current => Plugin.Configuration;

    public string DataFolderPath => Plugin.DataFolderPath;

    private static CurrentsPlugin Plugin =>
        CurrentsPlugin.Instance ?? throw new InvalidOperationException("The Currents plugin has not been initialised.");
}
