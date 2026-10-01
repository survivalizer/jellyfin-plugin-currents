using Jellyfin.Plugin.Currents.Configuration;
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
}
