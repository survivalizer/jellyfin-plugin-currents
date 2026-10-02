using Jellyfin.Plugin.Currents.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Currents;

/// <summary>Entry point Jellyfin loads for the Currents plugin.</summary>
public class CurrentsPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("483F31F4-DB4A-4900-A3B5-EE0FBA5084AC");

    public CurrentsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        if (string.IsNullOrEmpty(Configuration.SigningSecret))
        {
            Configuration.SigningSecret = Library.StrmSigner.NewSecret();
            SaveConfiguration();
        }
    }

    public static CurrentsPlugin? Instance { get; private set; }

    public static string UserAgent { get; } =
        $"Currents/{typeof(CurrentsPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    public override string Name => "Currents";

    public override Guid Id => PluginId;

    public override string Description => "AIOStreams & AIOMetadata for Jellyfin.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        };
    }
}
