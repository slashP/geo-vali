using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GeoVali.Tests.Support;

/// <summary>
/// A real loopback server standing in for whatever already owns the dashboard port — another
/// GeoVali, an unrelated program, or something that accepts the connection and then stalls.
/// </summary>
public sealed class StubPortOwner : IAsyncDisposable
{
    private WebApplication _app = null!;

    public int Port { get; private set; }

    public static async Task<StubPortOwner> StartAsync(string json, int status = 200, TimeSpan? delay = null)
    {
        var owner = new StubPortOwner();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapGet("/api/ping", async (HttpResponse response, CancellationToken ct) =>
        {
            if (delay is not null)
            {
                await Task.Delay(delay.Value, ct);
            }

            response.StatusCode = status;
            response.ContentType = "application/json";
            await response.WriteAsync(json, ct);
        });

        await app.StartAsync();
        owner._app = app;
        owner.Port = new Uri(app.Urls.First()).Port;
        return owner;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
