using GeoVali.Maps;

namespace GeoVali.Web;

public sealed record FolderEntry(string name, string path, bool hasMapJson, int mapCountBelow);

public sealed record FolderListing(string? path, string? parent, IReadOnlyList<FolderEntry> entries);

/// <summary>
/// A browser cannot open a native directory dialog for a server, so GeoVali renders its own
/// picker. Each row says whether that folder is a map and how many maps are under it, which is
/// how a non-developer confirms they picked the right tree.
/// </summary>
public static class FolderBrowser
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    public static FolderListing List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new FolderListing(null, null, Roots());
        }

        var full = Path.GetFullPath(path);

        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(full).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return new FolderListing(full, Path.GetDirectoryName(full), []);
        }

        var entries = children
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Select(directory => new FolderEntry(
                name: new DirectoryInfo(directory).Name,
                path: directory,
                hasMapJson: File.Exists(MapPaths.Definition(directory)),
                mapCountBelow: CountMaps(directory)))
            .ToList();

        return new FolderListing(full, Path.GetDirectoryName(full), entries);
    }

    private static int CountMaps(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, MapPaths.DefinitionFileName, Options).Count();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<FolderEntry> Roots()
    {
        var roots = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            roots.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName));
        }
        else
        {
            roots.Add("/");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            roots.Insert(0, home);
        }

        return roots
            .Distinct(StringComparer.Ordinal)
            .Select(root => new FolderEntry(root, root, File.Exists(MapPaths.Definition(root)), CountMaps(root)))
            .ToList();
    }
}
