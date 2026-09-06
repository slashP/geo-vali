using System.Net;

namespace GeoVali.Web;

/// <summary>
/// The dashboard is a local tool. Two guards: the request must come from loopback, and any
/// mutating call must carry a custom header. A page on another origin cannot set a custom header
/// without a CORS preflight, and GeoVali answers none, so it cannot drive the tool.
/// </summary>
public static class LocalOnlyMiddleware
{
    public const string RequiredHeader = "X-GeoVali";

    public static IApplicationBuilder UseLocalOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "GeoVali only accepts local connections." });
                return;
            }

            var isMutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            if (isMutating && !context.Request.Headers.ContainsKey(RequiredHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = $"Missing {RequiredHeader} header." });
                return;
            }

            await next();
        });
}
