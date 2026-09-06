using System.Net;

namespace GeoVali.Geoguessr;

/// <summary>
/// Retries a transient GeoGuessr failure a few times with exponential backoff.
/// Two deliberate exclusions: <c>POST</c> is never retried, because a lost response to
/// <c>POST /drafts</c> followed by a retry would leave the user with two maps; and 401 is never
/// retried, because an expired cookie aborts the whole run rather than being a blip.
/// </summary>
public sealed class TransientRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null) : DelegatingHandler
{
    public const int MaxRetries = 3;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((duration, ct) => Task.Delay(duration, ct));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException e)
            {
                transportFailure = e;
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                // The HttpClient timeout fired, not the caller's cancellation.
                transportFailure = e;
            }

            if (attempt >= MaxRetries || request.Method == HttpMethod.Post)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            response?.Dispose();
            await _delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            attempt++;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)status >= 500;
}
