namespace Jellyfin.Plugin.Currents.Common;

/// <summary>One recorded problem.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Area">Where: Sync, Streams, Skip markers, Collections.</param>
/// <param name="Message">What happened, masked.</param>
public sealed record DiagnosticEvent(DateTimeOffset At, string Area, string Message);
