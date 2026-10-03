using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class CompatStateTests
{
    private readonly FakeSettings _settings = new();

    [Theory]
    [InlineData("12.0.0", true)]
    [InlineData("12.1.0", true)]
    [InlineData("12.9.9.9", true)]
    [InlineData("13.0.0", false)]
    [InlineData("14.2.1", false)]
    [InlineData("11.9.0", false)]
    public void Knows_the_tested_range(string version, bool tested)
    {
        var compat = new CompatState(Version.Parse(version), _settings);

        Assert.Equal(tested, compat.InTestedRange);
        Assert.Equal(tested, compat.Active);
    }

    [Fact]
    public void An_unknown_version_counts_as_tested() => Assert.True(new CompatState(null, _settings).Active);

    [Fact]
    public void Force_enable_takes_effect_without_a_restart()
    {
        var compat = new CompatState(new Version(13, 0, 0), _settings);
        Assert.False(compat.Active);

        _settings.Current.ForceEnableOnUntestedServer = true;

        Assert.True(compat.ForceEnabled);
        Assert.True(compat.Active);
        Assert.False(compat.InTestedRange);
    }

    [Theory]
    [InlineData("13.0.0", "13.0.0")]
    [InlineData("not a version", "12.1.0")]
    [InlineData(null, "12.1.0")]
    [InlineData("", "12.1.0")]
    public void The_test_variable_overrides_the_reported_version(string? overrideValue, string expected) =>
        Assert.Equal(Version.Parse(expected), CompatState.Detect(new Version(12, 1, 0), overrideValue));
}
