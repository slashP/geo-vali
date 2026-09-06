using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class GeoguessrClientTests
{
    private static readonly MapAvatar Avatar = new()
    {
        background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese"
    };

    private static readonly IReadOnlyList<ValiLocation> FiveLocations =
    [
        new() { lat = 1, lng = 2, heading = 10, panoId = "pano-a" },
        new() { lat = 3, lng = 4, heading = 20, panoId = "pano-b" },
        new() { lat = 5, lng = 6, heading = 30, panoId = "" },
        new() { lat = 7, lng = 8, heading = 40, panoId = null },
        new() { lat = 9, lng = 10, heading = 50, panoId = "pano-e" }
    ];

    private static (GeoguessrClient client, RecordingHandler handler) Build(string cookie = "cookie-value")
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(GeoguessrClient.BaseAddress) };
        http.DefaultRequestHeaders.Add("Cookie", $"_ncfa={cookie}");
        return (new GeoguessrClient(http), handler);
    }

    [Fact]
    public async Task Existing_map_makes_exactly_three_calls_in_order_and_puts_version_plus_one()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"Coastal Sri Lanka","version":41}""") // GET draft
               .Enqueue(HttpStatusCode.OK)                                                                // PUT draft
               .Enqueue(HttpStatusCode.OK);                                                               // PUT publish

        var id = await client.PublishAsync(
            new PublishRequest("map-1", "Coastal Sri Lanka", "1234 locations.", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.Equal("map-1", id);
        Assert.Equal(3, handler.Calls.Count);

        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1", handler.Calls[0].PathAndQuery);

        Assert.Equal(HttpMethod.Put, handler.Calls[1].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1", handler.Calls[1].PathAndQuery);

        Assert.Equal(HttpMethod.Put, handler.Calls[2].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1/publish", handler.Calls[2].PathAndQuery);

        // The one quirk that is silent when wrong: version must be the read value plus one.
        var put = JsonNode.Parse(handler.Calls[1].Body)!;
        Assert.Equal(42, put["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task New_map_creates_a_draft_first_making_four_calls()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"new-map"}""")                          // POST drafts
               .Enqueue(HttpStatusCode.OK, """{"id":"new-map","name":"Fresh","version":0}""") // GET draft
               .Enqueue(HttpStatusCode.OK)                                                    // PUT draft
               .Enqueue(HttpStatusCode.OK);                                                   // PUT publish

        var id = await client.PublishAsync(
            new PublishRequest(null, "Fresh", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.Equal("new-map", id);
        Assert.Equal(4, handler.Calls.Count);

        Assert.Equal(HttpMethod.Post, handler.Calls[0].Method);
        Assert.Equal("/api/v4/user-maps/drafts", handler.Calls[0].PathAndQuery);
        var created = JsonNode.Parse(handler.Calls[0].Body)!;
        Assert.Equal("Fresh", created["name"]!.GetValue<string>());
        Assert.Equal("coordinates", created["mode"]!.GetValue<string>());

        var put = JsonNode.Parse(handler.Calls[2].Body)!;
        Assert.Equal(1, put["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task Unpublished_map_updates_the_draft_but_never_calls_publish()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"Draft only","version":3}""")
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "Draft only", "d", Avatar, Publish: false, FiveLocations),
            CancellationToken.None);

        Assert.Equal(2, handler.Calls.Count);
        Assert.DoesNotContain(handler.Calls, c => c.PathAndQuery.EndsWith("/publish"));
    }

    [Fact]
    public async Task Maps_locations_to_custom_coordinates_and_omits_an_empty_pano_id()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        var coordinates = JsonNode.Parse(handler.Calls[1].Body)!["customCoordinates"]!.AsArray();
        Assert.Equal(5, coordinates.Count);
        Assert.Equal(1, coordinates[0]!["lat"]!.GetValue<double>());
        Assert.Equal(2, coordinates[0]!["lng"]!.GetValue<double>());
        Assert.Equal(10, coordinates[0]!["heading"]!.GetValue<double>());
        Assert.Equal(0, coordinates[0]!["pitch"]!.GetValue<double>());
        Assert.Equal("pano-a", coordinates[0]!["panoId"]!.GetValue<string>());

        // An empty or missing panoId is sent as null, not as "".
        Assert.Null(coordinates[2]!["panoId"]?.GetValue<string>());
        Assert.Null(coordinates[3]!["panoId"]?.GetValue<string>());
    }

    [Fact]
    public async Task Sends_the_name_description_avatar_and_published_flag()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "Coastal Sri Lanka", "1234 hand-picked coastal locations.",
                Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        var body = JsonNode.Parse(handler.Calls[1].Body)!;
        Assert.Equal("map-1", body["id"]!.GetValue<string>());
        Assert.Equal("Coastal Sri Lanka", body["name"]!.GetValue<string>());
        Assert.Equal("1234 hand-picked coastal locations.", body["description"]!.GetValue<string>());
        Assert.True(body["published"]!.GetValue<bool>());
        Assert.Equal("evening", body["avatar"]!["background"]!.GetValue<string>());
        Assert.Equal("skyline", body["avatar"]!["landscape"]!.GetValue<string>());
        Assert.Equal("yellow", body["avatar"]!["ground"]!.GetValue<string>());
        Assert.Equal("japanese", body["avatar"]!["decoration"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sends_the_ncfa_cookie_on_every_call()
    {
        var (client, handler) = Build("secret-cookie");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.All(handler.Calls, call => Assert.Equal("_ncfa=secret-cookie", call.CookieHeader));
    }

    [Fact]
    public async Task A_401_during_publish_raises_the_auth_exception()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<GeoguessrAuthException>(() => client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_put_reports_the_status_and_the_response_body()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.BadRequest, """{"message":"Too few coordinates"}""");

        var exception = await Assert.ThrowsAsync<GeoguessrException>(() => client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None));

        Assert.Contains("400", exception.Message);
        Assert.Contains("Too few coordinates", exception.Message);
    }

    [Fact]
    public async Task Auth_probe_returns_the_signed_in_nick()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"user":{"nick":"slashP","id":"u1"}}""");

        var nick = await client.GetSignedInUserNickAsync(CancellationToken.None);

        Assert.Equal("slashP", nick);
        Assert.Equal("/api/v3/profiles", handler.Calls.Single().PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Calls.Single().Method);
    }

    [Fact]
    public async Task Auth_probe_returns_null_for_an_expired_cookie()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        Assert.Null(await client.GetSignedInUserNickAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Get_draft_yields_the_name_for_the_link_existing_flow()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-9","name":"An Arbitrary Africa","version":306}""");

        var draft = await client.GetDraftAsync("map-9", CancellationToken.None);

        Assert.Equal("An Arbitrary Africa", draft!.name);
        Assert.Equal(306, draft.version);
    }

    [Fact]
    public async Task Get_draft_returns_null_when_the_user_does_not_own_the_map()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        Assert.Null(await client.GetDraftAsync("someone-elses-map", CancellationToken.None));
    }
}
