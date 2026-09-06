namespace GeoVali.Maps;

/// <summary>A map cannot be published, with a message written for the user rather than for a log.</summary>
public sealed class NotPublishableException(string message) : Exception(message);

/// <summary>
/// GeoGuessr rejects a map with fewer than <see cref="MinimumLocationCount"/> coordinates with a
/// bare 400, which surfaces in the dashboard as an unreadable "Bad Request". Stop before the
/// request and say what is actually wrong instead.
/// </summary>
public static class MapPublishGuard
{
    public const int MinimumLocationCount = 5;

    public static void EnsurePublishable(string mapName, int locationCount)
    {
        if (locationCount < MinimumLocationCount)
        {
            throw new NotPublishableException(
                $"Refusing to publish \"{mapName}\": vali produced {locationCount} locations, " +
                $"and GeoGuessr needs at least {MinimumLocationCount}. " +
                "Widen the map definition in map.json and try again.");
        }
    }
}
