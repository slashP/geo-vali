using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoVali.Tests.Support;

/// <summary>
/// A real HTTP server on a loopback port that answers the four GeoGuessr calls GeoVali makes.
/// Lets the end-to-end test exercise the production HttpClient stack rather than a handler stub.
/// </summary>
public sealed class StubGeoguessrServer : IAsyncDisposable
{
    private WebApplication _app = null!;

    public string BaseAddress { get; private set; } = "";

    public List<(string Method, string Path, string Body)> Calls { get; } = [];

    /// <summary>When true every call answers 401, simulating an expired cookie.</summary>
    public bool Unauthorized { get; set; }

    /// <summary>Map id to the version the GET should report. Missing means 0.</summary>
    public Dictionary<string, int> DraftVersions { get; } = new(StringComparer.Ordinal);

    /// <summary>Map ids whose PUT should fail with 500, to exercise per-map failure isolation.</summary>
    public HashSet<string> RejectUpdateForMapIds { get; } = new(StringComparer.Ordinal);

    public static async Task<StubGeoguessrServer> StartAsync()
    {
        var server = new StubGeoguessrServer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            lock (server.Calls)
            {
                server.Calls.Add((context.Request.Method, context.Request.Path.Value ?? "", body));
            }

            if (server.Unauthorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.MapGet("/api/v3/profiles", () => Results.Json(new { user = new { nick = "slashP", id = "u1" } }));

        app.MapPost("/api/v4/user-maps/drafts", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var name = JsonNode.Parse(await reader.ReadToEndAsync())?["name"]?.GetValue<string>() ?? "";
            var id = $"created-{name.Replace(" ", "-").ToLowerInvariant()}";
            return Results.Json(new { id, name });
        });

        app.MapGet("/api/v4/user-maps/drafts/{id}", (string id) =>
            Results.Json(new { id, name = id, version = server.DraftVersions.GetValueOrDefault(id, 0) }));

        app.MapPut("/api/v4/user-maps/drafts/{id}", (string id) =>
            server.RejectUpdateForMapIds.Contains(id)
                ? Results.Json(new { message = "Something went wrong" }, statusCode: StatusCodes.Status500InternalServerError)
                : Results.Json(new { id }));

        app.MapPut("/api/v4/user-maps/drafts/{id}/publish", (string id) => Results.Json(new { id }));

        await app.StartAsync();
        server._app = app;
        server.BaseAddress = app.Urls.First().TrimEnd('/') + "/";
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
