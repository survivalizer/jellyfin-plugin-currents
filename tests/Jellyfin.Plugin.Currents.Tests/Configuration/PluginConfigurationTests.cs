using Jellyfin.Plugin.Currents.Configuration;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Configuration;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var config = new PluginConfiguration();

        Assert.Equal("http://127.0.0.1:8096", config.StrmBaseUrl);
        Assert.Equal(3, config.PruneAfterMisses);
        Assert.Equal(3, config.FailoverAttempts);
        Assert.Equal(5, config.AioStreamsPermitsPer10Seconds);
        Assert.Equal(15, config.AioMetadataPermitsPer5Seconds);
        Assert.Empty(config.Catalogs);
    }

    [Fact]
    public void Catalog_key_combines_type_and_id()
    {
        var catalog = new CatalogSelection { Type = "movie", Id = "tmdb.top" };

        Assert.Equal("movie/tmdb.top", catalog.Key);
    }

    [Fact]
    public void M2_defaults_match_the_spec()
    {
        var config = new PluginConfiguration();

        Assert.True(config.EnableVersions);
        Assert.True(config.AllowSelfService);
        Assert.False(config.DefaultAutoSelect);
        Assert.Equal(60, config.StreamCacheMinutes);
        Assert.Equal(20, config.MaxVersions);
        Assert.Equal(24, config.VersionTokenHours);
        Assert.Empty(config.DefaultPreferences.ResolutionOrder);
    }

    [Fact]
    public void Default_preferences_survive_jellyfins_xml_serializer()
    {
        var config = new PluginConfiguration
        {
            DefaultPreferences = new StreamPreferences { CachedOnly = true, MaxSizeGb = 25.5, ResolutionOrder = ["2160p", "1080p"], Hdr = HdrPreference.Avoid, AudioLanguages = ["English"] },
        };
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        using var reader = new StringReader(writer.ToString());

        var read = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.True(read.DefaultPreferences.CachedOnly);
        Assert.Equal(25.5, read.DefaultPreferences.MaxSizeGb);
        Assert.Equal(new[] { "2160p", "1080p" }, read.DefaultPreferences.ResolutionOrder);
        Assert.Equal(HdrPreference.Avoid, read.DefaultPreferences.Hdr);
        Assert.Equal(new[] { "English" }, read.DefaultPreferences.AudioLanguages);
    }
}
