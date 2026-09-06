namespace GeoVali.Maps;

/// <summary>A discovered map folder. <paramref name="IsConfigured"/> is false until it has a geoguessr.json.</summary>
public sealed record MapFolder(string Directory, string FolderName, bool IsConfigured);

/// <summary>Recursively finds map folders under a root.</summary>
public static class MapScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        // A permission-denied folder somewhere under the root must not abort the whole scan.
        IgnoreInaccessible = true,
        // Symlink loops in a maps tree would otherwise hang the scan.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        MatchType = MatchType.Simple
    };

    public static IReadOnlyList<MapFolder> Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, MapPaths.DefinitionFileName, Options)
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .Select(directory => new MapFolder(
                Directory: directory,
                FolderName: new DirectoryInfo(directory).Name,
                IsConfigured: File.Exists(MapPaths.Metadata(directory))))
            .ToList();
    }
}
