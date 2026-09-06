using System.ComponentModel;
using System.Diagnostics;

namespace GeoVali.Autostart;

/// <summary>Raised when a start-at-login mechanism could not be registered.</summary>
public sealed class AutostartException(string message) : Exception(message);

/// <summary>
/// Windows: a Scheduled Task registered through schtasks.exe. It runs at logon with /np — no
/// stored password, no interactive token — so GeoVali comes up with no console window to keep
/// open and no browser in your face. The user never types any of these commands.
/// </summary>
/// <param name="run">schtasks arguments in, exit code out. Substituted in tests.</param>
public sealed class WindowsTaskAutostart(string executablePath, Func<IReadOnlyList<string>, int> run) : IAutostart
{
    public const string TaskName = "GeoVali";

    public bool IsEnabled() => run(["/query", "/tn", TaskName]) == 0;

    public void Enable()
    {
        var exitCode = run([
            "/create", "/tn", TaskName,
            "/tr", $"\"{executablePath}\" --no-browser",
            "/sc", "onlogon",
            "/np",  // no stored password: the task runs without an interactive token, so no window
            "/f"    // overwrite, which keeps Enable idempotent
        ]);

        if (exitCode != 0)
        {
            throw new AutostartException(
                $"schtasks refused to register the {TaskName} task (exit code {exitCode}).");
        }
    }

    public void Disable() => _ = run(["/delete", "/tn", TaskName, "/f"]);  // absent: nothing to undo

    public string Describe() =>
        "Registers a Scheduled Task that starts GeoVali when you sign in — no window to keep open " +
        "and no browser. Run `geovali` any time to open the dashboard.";

    /// <summary>The real schtasks.exe. Output is swallowed; only the exit code matters.</summary>
    public static int RunSchtasks(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new AutostartException("schtasks.exe could not be started.");
        process.WaitForExit();
        return process.ExitCode;
    }
}

/// <summary>
/// What Windows actually uses: the Scheduled Task, falling back to a Startup-folder shortcut on a
/// machine where policy forbids registering tasks. Only ever one of the two is left in place.
/// </summary>
public sealed class WindowsAutostart(WindowsTaskAutostart task, WindowsStartupShortcut shortcut) : IAutostart
{
    public bool IsEnabled() => task.IsEnabled() || shortcut.IsEnabled();

    public void Enable()
    {
        try
        {
            task.Enable();
            shortcut.Disable();
        }
        catch (Exception e) when (e is AutostartException or Win32Exception)
        {
            shortcut.Enable();
        }
    }

    public void Disable()
    {
        task.Disable();
        shortcut.Disable();
    }

    public string Describe() =>
        shortcut.IsEnabled() && !task.IsEnabled()
            ? shortcut.Describe() + " A Scheduled Task would have been tidier, but this machine " +
              "would not register one, so expect a console window at sign-in."
            : task.Describe();
}
