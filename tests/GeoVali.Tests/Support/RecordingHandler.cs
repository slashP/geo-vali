using System.Net;
using System.Text;

namespace GeoVali.Tests.Support;

/// <summary>
/// Stands in for the GeoGuessr API. Records every call in order and returns queued responses,
/// so a test can assert the exact request sequence and the bodies that were sent.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public sealed record Call(HttpMethod Method, string PathAndQuery, string Body, string? CookieHeader);

    public List<Call> Calls { get; } = [];

    public RecordingHandler Enqueue(HttpStatusCode status, string json = "{}")
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues("Cookie", out var cookies);
        Calls.Add(new Call(request.Method, request.RequestUri!.PathAndQuery, body, cookies?.FirstOrDefault()));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
