using GeoVali.Maps;
using GeoVali.Vali;

namespace GeoVali.Tests.Support;

/// <summary>An in-memory stand-in for vali. No process, no data download.</summary>
public class FakeValiRunner : IValiRunner
{
    /// <summary>Set to null to simulate vali not being installed.</summary>
    public string? Executable { get; set; } = "/fake/vali";

    /// <summary>Map directory to the JSON that "vali" should write there. Missing means an empty array.</summary>
    public Dictionary<string, string> LocationsByDirectory { get; } = new(StringComparer.Ordinal);

    /// <summary>Map directory to the exit code to return. Missing means 0.</summary>
    public Dictionary<string, int> ExitCodeByDirectory { get; } = new(StringComparer.Ordinal);

    public List<string> GeneratedDirectories { get; } = [];

    public string? FindExecutable() => Executable;

    public virtual Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)
    {
        if (Executable is null)
        {
            throw new ValiNotFoundException($"vali was not found on your PATH. Install it with:  {ValiRunner.InstallCommand}");
        }

        GeneratedDirectories.Add(mapDirectory);
        onOutput($"Generating {Path.GetFileName(mapDirectory)}");

        var exitCode = ExitCodeByDirectory.GetValueOrDefault(mapDirectory, 0);
        if (exitCode == 0)
        {
            File.WriteAllText(
                MapPaths.Locations(mapDirectory),
                LocationsByDirectory.GetValueOrDefault(mapDirectory, "[]"));
        }

        return Task.FromResult(new ValiResult(exitCode, exitCode == 0 ? "Done." : "Country code XX is not valid."));
    }
}
