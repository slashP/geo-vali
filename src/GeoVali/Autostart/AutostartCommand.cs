using System.ComponentModel;

namespace GeoVali.Autostart;

/// <summary>
/// All the elevated copy of GeoVali does: register or remove the logon task, say what went wrong,
/// and exit. It never binds a port or opens a browser, so the toggle costs one UAC prompt and a
/// process that lives for a fraction of a second.
/// </summary>
public static class AutostartCommand
{
    /// <summary>True when <paramref name="argument"/> is one of the switches this command handles.</summary>
    public static bool Handles(string argument) =>
        argument.Equals(WindowsTaskAutostart.RegisterSwitch, StringComparison.OrdinalIgnoreCase)
        || argument.Equals(WindowsTaskAutostart.UnregisterSwitch, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the switch against <paramref name="autostart"/> and returns a process exit code.</summary>
    public static int Run(string argument, IAutostart autostart)
    {
        try
        {
            if (argument.Equals(WindowsTaskAutostart.RegisterSwitch, StringComparison.OrdinalIgnoreCase))
            {
                autostart.Enable();
            }
            else
            {
                autostart.Disable();
            }

            return 0;
        }
        catch (Exception e) when (e is AutostartException or Win32Exception)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
