namespace GeoVali.Maps;

/// <summary>
/// Single source of truth for how often a map regenerates and re-uploads.
/// Cadence is measured in whole <em>local calendar days</em> and gated on the local date, so
/// "every 1 day" means "regenerate if it wasn't already done today" regardless of how long the
/// previous run took. Ported from the author's MapCadence, minus the folder-name special cases.
/// </summary>
public static class Cadence
{
    /// <summary>Used when a map does not set <c>updateFrequencyDays</c> and the user has not changed the setting.</summary>
    public const int DefaultDaysBetweenUpdates = 7;

    /// <summary>Resolution order: per-map override, then the global default.</summary>
    public static int DaysBetweenUpdates(int? perMapOverrideDays, int globalDefaultDays) =>
        perMapOverrideDays is > 0 ? perMapOverrideDays.Value : globalDefaultDays;

    /// <summary>
    /// True when at least <paramref name="daysBetweenUpdates"/> local calendar days have elapsed
    /// since <paramref name="lastPublishedTimeUtc"/>.
    /// </summary>
    public static bool IsDue(DateTime lastPublishedTimeUtc, int daysBetweenUpdates, DateTime nowLocal)
    {
        if (lastPublishedTimeUtc == DateTime.MinValue)
        {
            return true;
        }

        var utc = lastPublishedTimeUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(lastPublishedTimeUtc, DateTimeKind.Utc)
            : lastPublishedTimeUtc;

        var lastLocalDate = DateOnly.FromDateTime(utc.ToLocalTime());
        var today = DateOnly.FromDateTime(nowLocal);
        return today.DayNumber - lastLocalDate.DayNumber >= daysBetweenUpdates;
    }
}
