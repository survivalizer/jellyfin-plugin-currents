namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Outcome of "Verify library".</summary>
/// <param name="Rewritten">Titles whose missing files were written again.</param>
/// <param name="Failed">Missing titles that could not be fetched or written (kept in state for the next run).</param>
/// <param name="UsersRemoved">users.json records of deleted Jellyfin users that were removed.</param>
public sealed record VerifyReport(int Rewritten, int Failed, int UsersRemoved);
