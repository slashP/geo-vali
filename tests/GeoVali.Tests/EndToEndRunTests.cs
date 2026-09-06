using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using GeoVali.Vali;
using Xunit;

namespace GeoVali.Tests;

public class EndToEndRunTests
{
    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private const string FourLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"}]
        """;

    private static GeoguessrMetadata Metadata(string name, string? id) => new()
    {
        id = id,
        name = name,
        description = "{{LocationCount}} locations.",
        avatar = new MapAvatar { background = "day", landscape = "hills", ground = "green", decoration = "none" },
        published = true
    };

    private static async Task<string> AddMap(TempDir temp, string folderName, GeoguessrMetadata metadata)
    {
        var dir = temp.Dir("maps", folderName);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        await MapMetadataStore.WriteMetadataAsync(dir, metadata);
        return dir;
    }

    private static UpdateRunner BuildRunner(TempDir temp, StubGeoguessrServer server, string fakeVali)
    {
        var http = new HttpClient(new TransientRetryHandler((_, _) => Task.CompletedTask)
        {
            InnerHandler = new HttpClientHandler()
        })
        {
            BaseAddress = new Uri(server.BaseAddress)
        };
        http.DefaultRequestHeaders.Add("Cookie", "_ncfa=test-cookie");

        var log = new RunLog(Path.Combine(temp.Path, "logs"), () => "test-cookie");
        return new UpdateRunner(
            new ValiRunner(fakeVali),
            new GeoguessrClient(http),
            log,
            () => Path.Combine(temp.Path, "maps"),
            () => 7,
            () => DateTime.Now);
    }

    [Fact]
    public async Task Generates_publishes_and_stamps_a_map_end_to_end()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.DraftVersions["africa-id"] = 306;

        var dir = await AddMap(temp, "africa", Metadata("An Arbitrary Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.maps.Single().succeeded);

        // vali really ran and really wrote the locations file.
        Assert.True(File.Exists(MapPaths.Locations(dir)));

        // The four-call sequence really went over the wire, in order, with version + 1.
        var paths = server.Calls.Select(c => $"{c.Method} {c.Path}").ToArray();
        Assert.Equal([
            "GET /api/v3/profiles",
            "GET /api/v4/user-maps/drafts/africa-id",
            "PUT /api/v4/user-maps/drafts/africa-id",
            "PUT /api/v4/user-maps/drafts/africa-id/publish"
        ], paths);
        Assert.Contains("\"version\":307", server.Calls[2].Body);
        Assert.Contains("\"description\":\"5 locations.\"", server.Calls[2].Body);

        // Stamped.
        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(1, ephemeral.updateCount);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task A_401_at_preflight_aborts_before_vali_runs()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.Unauthorized = true;

        var dir = await AddMap(temp, "africa", Metadata("An Arbitrary Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.True(result.authInvalid);

        // The whole point: nothing was regenerated, so no time was wasted on a stale cookie.
        Assert.False(File.Exists(MapPaths.Locations(dir)));
        Assert.Single(server.Calls);
        Assert.Equal("/api/v3/profiles", server.Calls.Single().Path);
    }

    [Fact]
    public async Task One_bad_map_never_stops_the_others()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.RejectUpdateForMapIds.Add("africa-id");

        var africa = await AddMap(temp, "africa", Metadata("Africa", "africa-id"));
        var europe = await AddMap(temp, "europe", Metadata("Europe", "europe-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single(m => m.directory == africa).succeeded);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);

        // Stamp only on success: africa stays due, europe does not.
        Assert.Equal(0, (await MapMetadataStore.ReadEphemeralAsync(africa)).updateCount);
        Assert.NotNull((await MapMetadataStore.ReadEphemeralAsync(africa)).lastError);
        Assert.Equal(1, (await MapMetadataStore.ReadEphemeralAsync(europe)).updateCount);
    }

    [Fact]
    public async Task Four_locations_fail_the_map_without_any_publish_call()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();

        var dir = await AddMap(temp, "thin", Metadata("Thin Map", "thin-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FourLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single().succeeded);
        Assert.Contains("at least 5", result.maps.Single().error);

        // Only the auth probe. No draft was read and nothing was written.
        Assert.Single(server.Calls);
        Assert.Equal(0, (await MapMetadataStore.ReadEphemeralAsync(dir)).updateCount);
    }

    [Fact]
    public async Task A_brand_new_map_is_created_and_its_id_written_back()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();

        var dir = await AddMap(temp, "fresh", Metadata("Fresh Map", id: null));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        await BuildRunner(temp, server, fakeVali).RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal("created-fresh-map", (await MapMetadataStore.ReadMetadataAsync(dir))!.id);
        Assert.Contains(server.Calls, c => c is { Method: "POST", Path: "/api/v4/user-maps/drafts" });
    }

    [Fact]
    public async Task The_cookie_never_appears_in_the_log_file()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        await AddMap(temp, "africa", Metadata("Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        await BuildRunner(temp, server, fakeVali).RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var logs = Directory.GetFiles(Path.Combine(temp.Path, "logs"), "geovali-*.log");
        Assert.All(logs, file => Assert.DoesNotContain("test-cookie", File.ReadAllText(file)));
    }
}
