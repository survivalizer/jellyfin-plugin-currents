using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeSettings : ICurrentsSettings
{
    public PluginConfiguration Current { get; set; } = new() { SigningSecret = Convert.ToBase64String(new byte[32]) };

    public string DataFolderPath { get; set; } = Path.Combine(Path.GetTempPath(), "currents-tests", Guid.NewGuid().ToString("N"));
}
