using System.Runtime.InteropServices;

namespace GeoVali.Autostart;

/// <summary>macOS: a LaunchAgent plist in ~/Library/LaunchAgents.</summary>
public sealed class MacAutostart(string executablePath, string stateDirectory) : IAutostart
{
    public const string Label = "com.geovali.agent";

    private string PlistPath => Path.Combine(stateDirectory, $"{Label}.plist");

    public bool IsEnabled() => File.Exists(PlistPath);

    public void Enable()
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(PlistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{Label}</string>
              <key>ProgramArguments</key>
              <array>
                <string>{executablePath}</string>
                <string>--no-browser</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>
            """);
    }

    public void Disable()
    {
        if (File.Exists(PlistPath))
        {
            File.Delete(PlistPath);
        }
    }

    public string Describe() =>
        "Installs a LaunchAgent in ~/Library/LaunchAgents, so GeoVali runs when you log in. " +
        "It takes effect at your next login, or after `launchctl load ~/Library/LaunchAgents/com.geovali.agent.plist`.";
}

/// <summary>Linux: a systemd user unit in ~/.config/systemd/user.</summary>
public sealed class LinuxAutostart(string executablePath, string stateDirectory) : IAutostart
{
    private string UnitPath => Path.Combine(stateDirectory, "geovali.service");

    public bool IsEnabled() => File.Exists(UnitPath);

    public void Enable()
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(UnitPath, $"""
            [Unit]
            Description=GeoVali - regenerate and republish vali GeoGuessr maps

            [Service]
            Type=simple
            ExecStart={executablePath} --no-browser
            Restart=on-failure

            [Install]
            WantedBy=default.target
            """);
    }

    public void Disable()
    {
        if (File.Exists(UnitPath))
        {
            File.Delete(UnitPath);
        }
    }

    public string Describe() =>
        "Installs a systemd user unit in ~/.config/systemd/user. " +
        "Activate it with `systemctl --user enable --now geovali`.";
}

/// <summary>Anything else. Never throws; the settings toggle simply stays off.</summary>
public sealed class UnsupportedAutostart : IAutostart
{
    public bool IsEnabled() => false;
    public void Enable() { }
    public void Disable() { }
    public string Describe() => "Start at login is not supported on this platform.";
}

public static class AutostartFactory
{
    /// <summary>The per-OS directory the unit or shortcut belongs in.</summary>
    public static string DefaultStateDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            // Windows registers a Scheduled Task and keeps no file of its own; this is only here
            // so every platform gets an answer.
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "LaunchAgents");
        }

        return Path.Combine(home, ".config", "systemd", "user");
    }

    /// <summary>The path of the running geovali executable, for the unit's ExecStart.</summary>
    public static string CurrentExecutablePath() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GeoVali");

    public static IAutostart Create(string executablePath, string stateDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsTaskAutostart(
                executablePath,
                WindowsTaskAutostart.RunSchtasks,
                WindowsTaskAutostart.IsRunningElevated,
                argument => WindowsTaskAutostart.RelaunchElevated(executablePath, argument));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacAutostart(executablePath, stateDirectory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxAutostart(executablePath, stateDirectory);
        }

        return new UnsupportedAutostart();
    }
}
