using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace GeoVali.Autostart;

/// <summary>Raised when a start-at-login mechanism could not be registered.</summary>
public sealed class AutostartException(string message) : Exception(message);

/// <summary>
/// Windows: a Scheduled Task registered through schtasks.exe. It runs at logon with /np — no
/// stored password, no interactive token — so GeoVali comes up with no console window to keep
/// open and no browser in your face.
///
/// Windows refuses a logon trigger outright on a filtered token: both /sc onlogon /np and plain
/// /sc onlogon come back "Access is denied" for a standard (or merely un-elevated) user. So the
/// toggle spends one UAC prompt, running a second copy of GeoVali elevated just long enough to
/// register the task. Signing in afterwards costs nothing — the task itself runs unprivileged.
/// </summary>
/// <param name="run">schtasks arguments in, exit code out. Substituted in tests.</param>
/// <param name="isElevated">Whether this process can register a logon trigger at all.</param>
/// <param name="relaunchElevated">Runs GeoVali again behind a UAC prompt, switch in, exit code out.</param>
public sealed class WindowsTaskAutostart(
    string executablePath,
    Func<IReadOnlyList<string>, int> run,
    Func<bool> isElevated,
    Func<string, int> relaunchElevated) : IAutostart
{
    public const string TaskName = "GeoVali";

    /// <summary>The switches the elevated copy is started with. See <see cref="AutostartCommand"/>.</summary>
    public const string RegisterSwitch = "--register-autostart";
    public const string UnregisterSwitch = "--unregister-autostart";

    private const int ErrorCancelled = 1223;   // the UAC dialog was dismissed

    /// <summary>Reading the task list needs no rights, so the settings screen can always answer this.</summary>
    public bool IsEnabled() => run(["/query", "/tn", TaskName]) == 0;

    public void Enable()
    {
        if (!isElevated())
        {
            Elevate(RegisterSwitch, "register");
            return;
        }

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

    public void Disable()
    {
        if (!isElevated())
        {
            Elevate(UnregisterSwitch, "remove");
            return;
        }

        _ = run(["/delete", "/tn", TaskName, "/f"]);  // absent: nothing to undo
    }

    public string Describe() =>
        "Registers a Scheduled Task that starts GeoVali when you sign in — no window to keep open " +
        "and no browser. Windows asks for administrator approval once, as you flip this switch; " +
        "signing in afterwards asks for nothing. Run `geovali` any time to open the dashboard.";

    /// <summary>One UAC prompt, one short-lived elevated copy of GeoVali, one exit code.</summary>
    private void Elevate(string argument, string verb)
    {
        int exitCode;

        try
        {
            exitCode = relaunchElevated(argument);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            throw new AutostartException(
                $"Windows needs administrator approval to {verb} a task that runs at sign-in, and the " +
                "prompt was dismissed. Nothing was changed.");
        }
        catch (Win32Exception e)
        {
            throw new AutostartException($"The administrator prompt could not be shown: {e.Message}");
        }

        if (exitCode != 0)
        {
            throw new AutostartException(
                $"The elevated copy of GeoVali could not {verb} the {TaskName} task " +
                $"(exit code {exitCode}).");
        }
    }

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

    /// <summary>
    /// Whether this process holds a full administrator token. Being a member of Administrators is
    /// not enough — UAC hands out a filtered token, and that is the one schtasks refuses.
    /// </summary>
    public static bool IsRunningElevated() =>
        OperatingSystem.IsWindows()
        && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts GeoVali again behind a UAC prompt and waits for it to finish.</summary>
    public static int RelaunchElevated(string executablePath, string argument)
    {
        var info = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,   // required for the runas verb
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new AutostartException("The administrator prompt could not be started.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
