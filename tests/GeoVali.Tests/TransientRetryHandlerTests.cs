using System.Net;
using GeoVali.Geoguessr;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class TransientRetryHandlerTests
{
    private static (HttpClient client, RecordingHandler inner, List<TimeSpan> delays) Build()
    {
        var inner = new RecordingHandler();
        var delays = new List<TimeSpan>();
        var retry = new TransientRetryHandler((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        })
        {
            InnerHandler = inner
        };

        return (new HttpClient(retry) { BaseAddress = new Uri("https://www.geoguessr.com/") }, inner, delays);
    }

    [Fact]
    public async Task Retries_a_server_error_and_succeeds()
    {
        var (client, inner, delays) = Build();
        inner.Enqueue(HttpStatusCode.BadGateway)
             .Enqueue(HttpStatusCode.ServiceUnavailable)
             .Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task Gives_up_after_three_retries()
    {
        var (client, inner, delays) = Build();
        for (var i = 0; i < 5; i++)
        {
            inner.Enqueue(HttpStatusCode.BadGateway);
        }

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(4, inner.Calls.Count); // one attempt plus MaxRetries
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Treats_timeouts_throttling_and_server_errors_as_transient(HttpStatusCode status)
    {
        var (client, inner, _) = Build();
        inner.Enqueue(status).Enqueue(HttpStatusCode.OK);

        var response = await client.GetAsync("api/v4/user-maps/drafts/map-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Never_retries_a_401_because_that_aborts_the_whole_run()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK);

        var response = await client.GetAsync("api/v3/profiles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Never_retries_a_400_because_the_request_is_wrong_not_unlucky()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.BadRequest).Enqueue(HttpStatusCode.OK);

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Never_retries_a_post_because_it_would_create_a_second_draft()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.BadGateway).Enqueue(HttpStatusCode.OK);

        var response = await client.PostAsync("api/v4/user-maps/drafts", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(inner.Calls);
    }
}
