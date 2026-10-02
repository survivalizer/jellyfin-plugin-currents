namespace Jellyfin.Plugin.Currents.Web;

/// <summary>The folders the admin should add to Jellyfin libraries.</summary>
public sealed record LibraryPathsResponse(string Movies, string Shows);
