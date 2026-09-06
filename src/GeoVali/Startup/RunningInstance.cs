using System.Text.Json;

namespace GeoVali.Startup;

/// <summary>
/// Finds a GeoVali that already owns the dashboard port, so a second start opens the running
/// dashboard instead of quietly bringing up a rival scheduler on the next port up.
/// Only the configured port is probed: if the running copy had to walk upwards, this misses it.
/// </summary>
public static class RunningInstance
{
    /// <returns>The base URL of the running GeoVali, or null if this port is free or foreign.</returns>
    public static async Task<string?> FindAsync(int port, TimeSpan timeout)
    {
        var url = $"http://127.0.0.1:{port}";

        try
        {
            using var http = new HttpClient { Timeout = timeout };
            using var response = await http.GetAsync($"{url}/api/ping");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var product = body.RootElement.TryGetProperty("product", out var value) ? value.GetString() : null;
            return product == AppInfo.ProductName ? url : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Nothing listening, something that is not GeoVali, or a port that stalls. All of them
            // mean the same thing here: carry on and start normally.
            return null;
        }
    }
}
