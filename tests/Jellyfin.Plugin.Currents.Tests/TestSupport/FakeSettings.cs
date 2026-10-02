using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeSettings : ICurrentsSettings
{
    public static readonly string Secret = Convert.ToBase64String(new byte[32]);

    public PluginConfiguration Current { get; set; } = new() { SigningSecret = Secret };

    public string DataFolderPath { get; set; } = Path.Combine(Path.GetTempPath(), "currents-tests", Guid.NewGuid().ToString("N"));
}
