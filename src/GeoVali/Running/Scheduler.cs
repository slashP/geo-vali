using Microsoft.Extensions.Hosting;
using GeoVali.Configuration;

namespace GeoVali.Running;

/// <summary>
/// Wakes on the configured interval and asks for a due-only run. It never forces a run: the
/// cadence decides which maps are actually touched.
/// </summary>
public sealed class Scheduler(RunCoordinator coordinator, ConfigStore config, RunLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.PruneOldFiles();

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, config.Current.checkIntervalMinutes));
            coordinator.NextScheduledRunUtc = DateTime.UtcNow.Add(interval);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.Current.mapsRoot))
            {
                continue; // Still in first-run setup.
            }

            try
            {
                await coordinator.RunAsync(new RunRequest(RunScope.DueOnly), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
