namespace GeoVali.Vali;

/// <summary>vali is not installed. Carries the exact command the user needs to run.</summary>
public sealed class ValiNotFoundException(string message) : Exception(message);

/// <summary>Outcome of one <c>vali generate</c>.</summary>
public sealed record ValiResult(int ExitCode, string LastOutputLine);

public interface IValiRunner
{
    /// <summary>Full path to the vali executable, or null when it is not installed.</summary>
    string? FindExecutable();

    /// <summary>
    /// Runs <c>vali generate --file &lt;mapDirectory&gt;/map.json</c>, which writes
    /// <c>map-locations.json</c> into the same directory.
    /// </summary>
    Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct);
}
