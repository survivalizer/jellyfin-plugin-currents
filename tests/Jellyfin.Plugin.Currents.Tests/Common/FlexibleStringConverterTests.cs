using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class FlexibleStringConverterTests
{
    private sealed class Holder
    {
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Value { get; set; }
    }

    [Theory]
    [InlineData("{\"value\":\"2019\"}", "2019")]
    [InlineData("{\"value\":2019}", "2019")]
    [InlineData("{\"value\":7.5}", "7.5")]
    [InlineData("{\"value\":true}", "true")]
    [InlineData("{\"value\":null}", null)]
    [InlineData("{\"value\":{\"nested\":1}}", null)]
    [InlineData("{\"value\":[1,2]}", null)]
    public void Reads_any_scalar_as_string(string json, string? expected)
    {
        var holder = JsonSerializer.Deserialize<Holder>(json, JsonDefaults.Options)!;

        Assert.Equal(expected, holder.Value);
    }
}
