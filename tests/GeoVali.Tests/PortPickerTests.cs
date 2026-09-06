using System.Net;
using System.Net.Sockets;
using GeoVali.Startup;
using Xunit;

namespace GeoVali.Tests;

public class PortPickerTests
{
    [Fact]
    public void Returns_the_preferred_port_when_it_is_free()
    {
        var free = FindAnUnusedPort();

        Assert.Equal(free, PortPicker.FindFree(free));
    }

    [Fact]
    public void Steps_to_the_next_port_when_the_preferred_one_is_taken()
    {
        var taken = FindAnUnusedPort();
        using var listener = new TcpListener(IPAddress.Loopback, taken);
        listener.Start();

        var chosen = PortPicker.FindFree(taken);

        Assert.NotEqual(taken, chosen);
        Assert.InRange(chosen, taken + 1, taken + 20);
    }

    [Fact]
    public void Throws_a_readable_error_when_nothing_in_the_range_is_free()
    {
        var first = FindAnUnusedPort();
        var listeners = new List<TcpListener>();
        try
        {
            for (var port = first; port < first + 3; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listeners.Add(listener);
            }

            var exception = Assert.Throws<InvalidOperationException>(() => PortPicker.FindFree(first, attempts: 3));

            Assert.Contains("No free port", exception.Message);
            Assert.Contains(first.ToString(), exception.Message);
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }
        }
    }

    private static int FindAnUnusedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
