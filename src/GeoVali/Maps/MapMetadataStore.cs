using System.Text.Json;

namespace GeoVali.Maps;

/// <summary>Reads and writes the two JSON files that live beside a map definition.</summary>
public static class MapMetadataStore
{
    private static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };

    /// <summary>Returns null when the map has no <c>geoguessr.json</c>, i.e. is not set up yet.</summary>
    public static async Task<GeoguessrMetadata?> ReadMetadataAsync(string directory)
    {
        var path = MapPaths.Metadata(directory);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GeoguessrMetadata>(stream, Read);
    }

    public static async Task WriteMetadataAsync(string directory, GeoguessrMetadata metadata)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(MapPaths.Metadata(directory), JsonSerializer.Serialize(metadata, Write));
    }

    /// <summary>
    /// Never throws. A missing or corrupt ephemeral file means "never published", which makes the
    /// map due — the safe direction, since the worst case is one redundant regeneration.
    /// </summary>
    public static async Task<EphemeralMetadata> ReadEphemeralAsync(string directory)
    {
        var path = MapPaths.Ephemeral(directory);
        if (!File.Exists(path))
        {
            return new EphemeralMetadata();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<EphemeralMetadata>(stream, Read) ?? new EphemeralMetadata();
        }
        catch (JsonException)
        {
            return new EphemeralMetadata();
        }
    }

    public static async Task WriteEphemeralAsync(string directory, EphemeralMetadata ephemeral)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(MapPaths.Ephemeral(directory), JsonSerializer.Serialize(ephemeral, Write));
    }
}
