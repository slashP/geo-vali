namespace GeoVali.Maps;

/// <summary>
/// A map is a folder. These are the four files that can live in it.
/// <c>vali generate --file &lt;dir&gt;/map.json</c> writes <c>map-locations.json</c> alongside the
/// definition, which is why the folder-as-map convention needs no configuration.
/// </summary>
public static class MapPaths
{
    /// <summary>Written by the user. The vali map definition. Source-controlled.</summary>
    public const string DefinitionFileName = "map.json";

    /// <summary>Written by GeoVali at setup, hand-edited afterwards. Source-controlled.</summary>
    public const string MetadataFileName = "geoguessr.json";

    /// <summary>Written by GeoVali every run. Regenerable, so not source-controlled.</summary>
    public const string EphemeralFileName = "geoguessr.ephemeral.json";

    /// <summary>Written by <c>vali generate</c>. Regenerable, so not source-controlled.</summary>
    public const string LocationsFileName = "map-locations.json";

    public static string Definition(string directory) => Path.Combine(directory, DefinitionFileName);
    public static string Metadata(string directory) => Path.Combine(directory, MetadataFileName);
    public static string Ephemeral(string directory) => Path.Combine(directory, EphemeralFileName);
    public static string Locations(string directory) => Path.Combine(directory, LocationsFileName);
}
