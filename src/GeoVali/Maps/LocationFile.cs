using System.Text.Json;

namespace GeoVali.Maps;

/// <summary>
/// Reads <c>map-locations.json</c>. A large map's file is a bare JSON array of tens of thousands
/// of objects and runs to double-digit megabytes, so it is streamed rather than read into a string.
/// </summary>
public static class LocationFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static async Task<IReadOnlyList<ValiLocation>> ReadAsync(string directory, CancellationToken ct)
    {
        var path = MapPaths.Locations(directory);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            return await JsonSerializer.DeserializeAsync<List<ValiLocation>>(stream, Options, ct) ?? [];
        }
        catch (JsonException e)
        {
            throw new NotPublishableException(
                $"{MapPaths.LocationsFileName} in {directory} could not be read as JSON " +
                $"({e.Message}). Delete it and run this map again to regenerate it.");
        }
    }
}
