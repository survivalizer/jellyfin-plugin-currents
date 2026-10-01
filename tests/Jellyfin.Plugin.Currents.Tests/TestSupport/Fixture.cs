namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal static class Fixture
{
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));
}
