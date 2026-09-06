using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GeoVali.Maps;

namespace GeoVali.Vali;

/// <param name="executableOverride">
/// An explicit path to vali. Null means "look it up on PATH and in the dotnet tools folder".
/// </param>
public sealed partial class ValiRunner(string? executableOverride = null) : IValiRunner
{
    /// <summary>What GeoVali tells the user to run when vali is missing. It never runs it for them.</summary>
    public const string InstallCommand = "dotnet tool install -g vali";

    // ESC [ ... <letter> — the SGR colour sequences vali wraps its banner in.
    [GeneratedRegex("\u001b\\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiEscape();

    /// <summary>vali prints a coloured banner; the dashboard must not show the escape codes.</summary>
    public static string StripAnsi(string line) => AnsiEscape().Replace(line, string.Empty);

    public string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(executableOverride))
        {
            return File.Exists(executableOverride) ? executableOverride : null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string[] fileNames = isWindows ? ["vali.exe", "vali.cmd", "vali.bat"] : ["vali"];

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"));

        return directories
            .SelectMany(directory => fileNames.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
    }

    public async Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)
    {
        var executable = FindExecutable()
            ?? throw new ValiNotFoundException(
                $"vali was not found on your PATH. Install it with:  {InstallCommand}");

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = mapDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("generate");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(MapPaths.Definition(mapDirectory));

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var lastLine = "";
        void Handle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var line = StripAnsi(raw).TrimEnd();
            if (line.Length == 0)
            {
                return;
            }

            lastLine = line;
            onOutput(line);
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ValiResult(process.ExitCode, lastLine);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
