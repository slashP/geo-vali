using System.Diagnostics;

namespace GeoVali.Startup;

public static class BrowserLauncher
{
    /// <summary>
    /// Opens the dashboard in the default browser. Never throws: a headless machine or a missing
    /// xdg-open must not stop the server, which is still reachable at the printed URL.
    /// </summary>
    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }

            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(opener, url) { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException or PlatformNotSupportedException)
        {
            Console.WriteLine($"Open {url} in your browser.");
        }
    }
}
