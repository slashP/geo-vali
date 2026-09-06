using GeoVali.Configuration;

namespace GeoVali.Geoguessr;

/// <summary>
/// Attaches <c>Cookie: _ncfa=&lt;value&gt;</c> to every GeoGuessr request, read fresh from the
/// credential store each time. Keeping it here rather than on the HttpClient's default headers
/// means a newly pasted cookie takes effect immediately, and the cookie exists in exactly one
/// place in the request path.
/// </summary>
public sealed class CookieHandler(CredentialStore credentials) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookie = credentials.ReadCookie();
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", $"_ncfa={cookie}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
