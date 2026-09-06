namespace GeoVali.Running;

public enum RunScope
{
    /// <summary>Only maps whose cadence has elapsed. What the scheduler asks for.</summary>
    DueOnly,

    /// <summary>Every configured map, cadence ignored. "Run everything" in the dashboard.</summary>
    All,

    /// <summary>One map, cadence ignored. The per-row "Run now".</summary>
    Single
}

public sealed record RunRequest(RunScope Scope, string? SingleMapDirectory = null);

public sealed record MapRunOutcome(string directory, string name, bool succeeded, bool skipped, string? error);

public sealed record RunResult(
    bool preflightFailed,
    string? preflightError,
    bool authInvalid,
    IReadOnlyList<MapRunOutcome> maps);
