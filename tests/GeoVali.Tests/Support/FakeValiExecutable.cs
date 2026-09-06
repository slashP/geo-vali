using System.Runtime.InteropServices;

namespace GeoVali.Tests.Support;

/// <summary>
/// Writes a script that behaves like <c>vali generate</c>: prints a line, drops a canned
/// map-locations.json next to the map.json it was pointed at, and exits with a chosen code.
/// Lets the runner and the end-to-end test exercise real process invocation without real vali.
/// </summary>
public static class FakeValiExecutable
{
    /// <returns>The full path to the executable script.</returns>
    public static string Create(TempDir temp, string locationsJson, int exitCode = 0, string stdout = "Generated locations.")
    {
        var payloadPath = Path.Combine(temp.Path, $"canned-{Guid.NewGuid():N}.json");
        File.WriteAllText(payloadPath, locationsJson);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmdPath = Path.Combine(temp.Path, $"fake-vali-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(cmdPath, $"""
                @echo off
                echo {stdout}
                rem args are: generate --file <path to map.json>
                for %%I in ("%~3") do set "MAPDIR=%%~dpI"
                copy /y "{payloadPath}" "%MAPDIR%map-locations.json" >nul
                exit /b {exitCode}
                """);
            return cmdPath;
        }

        var shPath = Path.Combine(temp.Path, $"fake-vali-{Guid.NewGuid():N}.sh");
        File.WriteAllText(shPath, $"""
            #!/bin/sh
            echo "{stdout}"
            # args are: generate --file <path to map.json>
            MAPDIR=$(dirname "$3")
            cp "{payloadPath}" "$MAPDIR/map-locations.json"
            exit {exitCode}
            """.ReplaceLineEndings("\n"));
        File.SetUnixFileMode(shPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shPath;
    }
}
