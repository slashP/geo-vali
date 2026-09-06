namespace GeoVali.Startup;

/// <summary>
/// What a starting GeoVali should do about the dashboard port: join the copy already running
/// there, or bind a port of its own.
/// </summary>
/// <param name="Port">The port to bind. Meaningless when <paramref name="AlreadyRunning"/>.</param>
/// <param name="Url">Where the dashboard is, either way.</param>
public sealed record StartupPlan(int Port, string Url, bool AlreadyRunning)
{
    public static async Task<StartupPlan> CreateAsync(int preferredPort, TimeSpan probeTimeout)
    {
        var running = await RunningInstance.FindAsync(preferredPort, probeTimeout);
        if (running is not null)
        {
            return new StartupPlan(preferredPort, running, AlreadyRunning: true);
        }

        // Nothing of ours there. Something else may still hold the port, so walk upwards.
        var port = PortPicker.FindFree(preferredPort);
        return new StartupPlan(port, $"http://127.0.0.1:{port}", AlreadyRunning: false);
    }
}
