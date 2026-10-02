namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Why a manifest URL was refused.</summary>
/// <param name="Message">The masked explanation.</param>
/// <param name="Remote">True when it carries text from the AIOStreams server (or the network); false for fixed parse errors.</param>
public sealed record ManifestProblem(string Message, bool Remote);
