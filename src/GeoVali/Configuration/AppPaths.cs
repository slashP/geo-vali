namespace GeoVali.Configuration;

/// <summary>
/// Application config lives outside the maps folder, in the platform location, so the maps tree
/// stays exactly what the user committed.
/// </summary>
public static class AppPaths
{
    public static string ConfigDirectory { get; } = Resolve();

    public static string ConfigFile => Path.Combine(ConfigDirectory, ConfigStore.FileName);
    public static string CredentialsFile => Path.Combine(ConfigDirectory, CredentialStore.FileName);
    public static string LogDirectory => Path.Combine(ConfigDirectory, "logs");

    private static string Resolve()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            // %APPDATA%\GeoVali\
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoVali");
        }

        if (OperatingSystem.IsMacOS())
        {
            // ~/Library/Application Support/GeoVali/
            // Not SpecialFolder.ApplicationData, which .NET maps to ~/.config on macOS too.
            return Path.Combine(home, "Library", "Application Support", "GeoVali");
        }

        // $XDG_CONFIG_HOME/geovali/, falling back to ~/.config/geovali/
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg;
        return Path.Combine(root, "geovali");
    }
}
