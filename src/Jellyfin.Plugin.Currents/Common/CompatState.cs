namespace Jellyfin.Plugin.Currents.Common;

/// <summary>
/// Whether Currents' Jellyfin integration (the version decorator, PlaybackInfo and segment filters) acts on this server.
/// Outside the tested Jellyfin range it stands down, so titles play their .strm (degraded mode), unless the admin forces it on.
/// The decision is read per call, so forcing it on needs no restart.
/// </summary>
public sealed class CompatState
{
    public const string TestVersionVariable = "CURRENTS_COMPAT_TEST_VERSION";
    private readonly ICurrentsSettings _settings;

    public CompatState(Version? server, ICurrentsSettings settings)
    {
        Server = server;
        _settings = settings;
        InTestedRange = server is null || (server >= TestedFrom && server < TestedBefore);
    }

    public static Version TestedFrom { get; } = new(12, 0);

    public static Version TestedBefore { get; } = new(13, 0);

    /// <summary>Gets the Jellyfin version, or null when it could not be read (counted as tested).</summary>
    public Version? Server { get; }

    public bool InTestedRange { get; }

    public bool ForceEnabled => _settings.Current.ForceEnableOnUntestedServer;

    public bool Active => InTestedRange || ForceEnabled;

    /// <summary>The version to judge: <paramref name="overrideValue"/> (from <see cref="TestVersionVariable"/>) when it parses, else the reported one.</summary>
    /// <param name="reported">The version Jellyfin reports.</param>
    /// <param name="overrideValue">The test override, if set.</param>
    /// <returns>The version.</returns>
    public static Version? Detect(Version? reported, string? overrideValue) =>
        Version.TryParse(overrideValue, out var forced) ? forced : reported;
}
