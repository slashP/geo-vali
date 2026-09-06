namespace GeoVali.Maps;

/// <summary>The four-part GeoGuessr map thumbnail. Generated once at setup, then left alone.</summary>
public sealed record MapAvatar
{
    public string background { get; init; } = "";
    public string landscape { get; init; } = "";
    public string ground { get; init; } = "";
    public string decoration { get; init; } = "";
}

/// <summary>
/// <c>geoguessr.json</c> — written by GeoVali when the map is set up, hand-edited afterwards,
/// and source-controlled by the user. Property names match the file exactly.
/// </summary>
public sealed record GeoguessrMetadata
{
    /// <summary>Absent until the map is first published, or filled immediately when linking an existing map.</summary>
    public string? id { get; init; }

    public string name { get; init; } = "";

    /// <summary>May contain the <c>{{LocationCount}}</c> token, which is expanded at publish time only.</summary>
    public string description { get; init; } = "";

    /// <summary>Null until GeoVali generates one; persisted afterwards so it is stable across runs.</summary>
    public MapAvatar? avatar { get; init; }

    /// <summary>When false, GeoVali updates the draft but never calls the publish endpoint.</summary>
    public bool published { get; init; } = true;

    /// <summary>Null means "use the global default".</summary>
    public int? updateFrequencyDays { get; init; }
}

/// <summary>
/// <c>geoguessr.ephemeral.json</c> — regenerable state, kept separate from the committed metadata
/// so the user can gitignore it.
/// </summary>
public sealed record EphemeralMetadata
{
    public DateTime lastPublishedTimeUtc { get; init; } = DateTime.MinValue;
    public int updateCount { get; init; }
    public DateTime? lastRunUtc { get; init; }
    public string? lastError { get; init; }
}

/// <summary>One entry of <c>map-locations.json</c>, as written by <c>vali generate</c>.</summary>
public sealed record ValiLocation
{
    public double lat { get; init; }
    public double lng { get; init; }
    public double heading { get; init; }

    /// <summary>vali omits this field; it defaults to 0, which is what GeoGuessr expects.</summary>
    public double pitch { get; init; }

    public string? panoId { get; init; }
}
