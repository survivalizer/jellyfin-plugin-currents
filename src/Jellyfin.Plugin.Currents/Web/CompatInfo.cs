namespace Jellyfin.Plugin.Currents.Web;

/// <summary>The Jellyfin version check.</summary>
/// <param name="Server">The Jellyfin version, if known.</param>
/// <param name="TestedRange">The tested range, for display.</param>
/// <param name="InTestedRange">The version is in the tested range.</param>
/// <param name="ForceEnabled">The admin forced Currents on.</param>
/// <param name="Active">The integration acts (in range or forced).</param>
public sealed record CompatInfo(string? Server, string TestedRange, bool InTestedRange, bool ForceEnabled, bool Active);
