using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Currents.Common;

/// <summary>Shared System.Text.Json options for external APIs and plugin state files.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonSerializerOptions Indented { get; } = new(Options) { WriteIndented = true };
}
