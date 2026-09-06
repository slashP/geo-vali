using GeoVali.Maps;

namespace GeoVali.Configuration;

/// <summary>Everything in <c>config.json</c>. Never holds the cookie — that lives in credentials.json.</summary>
public sealed record AppConfig
{
    /// <summary>Null until the user has picked a maps folder in the first-run screen.</summary>
    public string? mapsRoot { get; init; }

    public int defaultCadenceDays { get; init; } = Cadence.DefaultDaysBetweenUpdates;
    public int checkIntervalMinutes { get; init; } = 30;
    public int dashboardPort { get; init; } = 5099;
    public bool startAtLogin { get; init; }

    /// <summary>Set only when vali is somewhere the PATH lookup does not find.</summary>
    public string? valiExecutablePath { get; init; }
}
