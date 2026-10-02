using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Plugin-managed library folders. Admins add Movies/ and Shows/ to Jellyfin libraries.</summary>
public sealed class LibraryPaths
{
    public LibraryPaths(string root) => Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public string Root { get; }

    public string Movies => Path.Combine(Root, "Movies");

    public string Shows => Path.Combine(Root, "Shows");

    public static LibraryPaths FromSettings(ICurrentsSettings settings) =>
        new(string.IsNullOrWhiteSpace(settings.Current.LibraryRoot)
            ? Path.Combine(settings.DataFolderPath, "library")
            : settings.Current.LibraryRoot);

    public string RootFor(MediaKind kind) => kind == MediaKind.Movie ? Movies : Shows;
}
