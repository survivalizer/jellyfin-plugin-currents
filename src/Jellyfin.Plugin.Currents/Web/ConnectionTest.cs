namespace Jellyfin.Plugin.Currents.Web;

/// <summary>One connection test.</summary>
/// <param name="Name">The service.</param>
/// <param name="Status">"ok", "failed" or "off".</param>
/// <param name="Message">What happened, masked.</param>
/// <param name="Milliseconds">How long it took.</param>
public sealed record ConnectionTest(string Name, string Status, string Message, long Milliseconds);
