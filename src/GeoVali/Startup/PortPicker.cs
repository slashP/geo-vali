using System.Net;
using System.Net.Sockets;

namespace GeoVali.Startup;

/// <summary>
/// The dashboard wants a stable port so the user can bookmark it, but must still start when
/// something else has claimed it. Try the configured port, then walk upwards.
/// </summary>
public static class PortPicker
{
    public static int FindFree(int preferred, int attempts = 20)
    {
        for (var port = preferred; port < preferred + attempts && port <= 65535; port++)
        {
            if (IsFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"No free port was found between {preferred} and {preferred + attempts - 1}. " +
            "Change the dashboard port in config.json and start GeoVali again.");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
