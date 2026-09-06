using System.Reflection;

namespace GeoVali;

/// <summary>Identity of the running tool, shown in the dashboard header and the log file.</summary>
public static class AppInfo
{
    public const string ProductName = "GeoVali";

    public static string Version { get; } =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";
}
