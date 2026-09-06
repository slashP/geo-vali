using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class UpdateRunnerTests
{
    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},
         {"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},
         {"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private const string FourLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},
         {"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},
         {"lat":7,"lng":8,"heading":40,"panoId":"d"}]
        """;

    private static readonly DateTime Now = new(2026, 9, 6, 3, 0, 0, DateTimeKind.Local);

    private sealed class Fixture : IDisposable
    {
        public TempDir Temp { get; } = new();
        public FakeValiRunner Vali { get; } = new();
        public FakeGeoguessrClient Geoguessr { get; } = new();
        public RunLog Log { get; }
        public int DefaultCadenceDays { get; set; } = 7;

        public Fixture() => Log = new RunLog(Path.Combine(Temp.Path, "logs"), () => null);

        public UpdateRunner Runner() => new(
            Vali, Geoguessr, Log,
            mapsRootProvider: () => Path.Combine(Temp.Path, "maps"),
            defaultCadenceProvider: () => DefaultCadenceDays,
            nowLocalProvider: () => Now);

        /// <summary>Creates a configured map folder and returns its directory.</summary>
        public async Task<string> AddMap(
            string folderName,
            GeoguessrMetadata metadata,
            EphemeralMetadata? ephemeral = null,
            string locations = FiveLocations)
        {
            var dir = Temp.Dir("maps", folderName);
            await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
            await MapMetadataStore.WriteMetadataAsync(dir, metadata);
            if (ephemeral is not null)
            {
                await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
            }

            Vali.LocationsByDirectory[dir] = locations;
            return dir;
        }

        /// <summary>Creates a folder with map.json but no geoguessr.json.</summary>
        public async Task<string> AddUnconfiguredMap(string folderName)
        {
            var dir = Temp.Dir("maps", folderName);
            await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
            return dir;
        }

        public void Dispose() => Temp.Dispose();
    }

    private static GeoguessrMetadata Metadata(string name, string? id = "existing-id", int? cadence = null, bool published = true) =>
        new()
        {
            id = id,
            name = name,
            description = "{{LocationCount}} locations.",
            avatar = new MapAvatar { background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese" },
            published = published,
            updateFrequencyDays = cadence
        };

    // ---- Preflight ----

    [Fact]
    public async Task Aborts_before_touching_any_map_when_vali_is_missing()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));
        fixture.Vali.Executable = null;

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.Contains("dotnet tool install -g vali", result.preflightError);
        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    [Fact]
    public async Task Checks_auth_before_generating_anything()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));
        fixture.Geoguessr.Nick = null; // expired cookie

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.True(result.authInvalid);
        // The whole point of the ordering: nothing was regenerated.
        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    [Fact]
    public async Task Probes_auth_exactly_once_per_run_not_once_per_map()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("Africa"));
        await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal(1, fixture.Geoguessr.AuthProbeCount);
    }

    // ---- Cadence and scope ----

    [Fact]
    public async Task Due_only_skips_a_map_published_today()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 5 });

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.True(result.maps.Single(m => m.directory == dir).skipped);
    }

    [Fact]
    public async Task Due_only_runs_a_map_whose_cadence_has_elapsed()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-11).ToUniversalTime(), updateCount = 5 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Equal([dir], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_everything_ignores_cadence()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 5 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal([dir], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Single_scope_runs_only_the_named_map_and_ignores_its_cadence()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 1 });
        await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.Single, africa), CancellationToken.None);

        Assert.Equal([africa], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Falls_back_to_the_global_default_cadence()
    {
        using var fixture = new Fixture();
        fixture.DefaultCadenceDays = 7;
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: null),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-6).ToUniversalTime(), updateCount = 1 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories); // 6 days < 7
    }

    [Fact]
    public async Task Ignores_a_folder_that_has_no_geoguessr_json()
    {
        using var fixture = new Fixture();
        await fixture.AddUnconfiguredMap("brand-new");

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    // ---- The happy path ----

    [Fact]
    public async Task Publishes_with_the_expanded_description_and_the_generated_locations()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var request = fixture.Geoguessr.Published.Single();
        Assert.Equal("An Arbitrary Africa", request.Name);
        Assert.Equal("5 locations.", request.Description);
        Assert.Equal(5, request.Locations.Count);
        Assert.True(request.Publish);
        Assert.Equal("existing-id", request.MapId);
    }

    [Fact]
    public async Task Leaves_the_token_unsubstituted_in_geoguessr_json()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("{{LocationCount}} locations.", metadata!.description);
    }

    [Fact]
    public async Task Stamps_the_ephemeral_file_on_success()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 306 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(307, ephemeral.updateCount);
        Assert.True(ephemeral.lastPublishedTimeUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.NotNull(ephemeral.lastRunUtc);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task Writes_the_new_id_back_when_a_map_is_created()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("brand-new", Metadata("Brand New", id: null));
        fixture.Geoguessr.NewMapId = "6a9d6c505a43d0a64f98be5c";

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("6a9d6c505a43d0a64f98be5c", metadata!.id);
    }

    [Fact]
    public async Task Generates_and_persists_an_avatar_when_the_map_has_none()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("no-avatar", Metadata("No Avatar") with { avatar = null });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.NotNull(metadata!.avatar);
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar!.background));
        Assert.Equal(metadata.avatar.background, fixture.Geoguessr.Published.Single().Avatar.background);
    }

    [Fact]
    public async Task An_unpublished_map_is_sent_with_publish_false()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("draft", Metadata("Draft Only", published: false));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(fixture.Geoguessr.Published.Single().Publish);
    }

    [Fact]
    public async Task Runs_maps_one_at_a_time_in_a_stable_order()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal([africa, europe], fixture.Vali.GeneratedDirectories);
    }

    // ---- Failure isolation ----

    [Fact]
    public async Task A_vali_failure_fails_that_map_and_moves_to_the_next()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Vali.ExitCodeByDirectory[africa] = 1;

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var failed = result.maps.Single(m => m.directory == africa);
        Assert.False(failed.succeeded);
        Assert.Contains("exit code 1", failed.error);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);
        Assert.Equal("Europe", fixture.Geoguessr.Published.Single().Name);
    }

    [Fact]
    public async Task Four_locations_fail_the_map_before_any_http_call()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("thin", Metadata("Thin Map"), locations: FourLocations);

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single().succeeded);
        Assert.Contains("at least 5", result.maps.Single().error);
        Assert.Empty(fixture.Geoguessr.Published);
    }

    [Fact]
    public async Task A_failed_map_is_not_stamped_so_it_stays_due()
    {
        using var fixture = new Fixture();
        var before = new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 306 };
        var dir = await fixture.AddMap("thin", Metadata("Thin Map"), before, locations: FourLocations);

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var after = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(before.lastPublishedTimeUtc, after.lastPublishedTimeUtc);
        Assert.Equal(306, after.updateCount);
        Assert.Contains("at least 5", after.lastError);
        Assert.NotNull(after.lastRunUtc);
    }

    [Fact]
    public async Task Clears_a_previous_error_after_a_successful_run()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("recovered", Metadata("Recovered"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 1, lastError = "old failure" });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Null((await MapMetadataStore.ReadEphemeralAsync(dir)).lastError);
    }

    [Fact]
    public async Task A_401_mid_run_aborts_the_whole_run_rather_than_failing_forty_maps()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Geoguessr.FailuresByMapName["Africa"] = new GeoguessrAuthException();

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.authInvalid);
        // Europe was never attempted, and the expired cookie is not recorded as its failure.
        Assert.DoesNotContain(result.maps, m => m.directory == europe);
        Assert.Equal([africa], fixture.Vali.GeneratedDirectories);
        Assert.Null((await MapMetadataStore.ReadEphemeralAsync(europe)).lastError);
    }

    [Fact]
    public async Task A_geoguessr_rejection_fails_only_that_map()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Geoguessr.FailuresByMapName["Africa"] = new GeoguessrException("GeoGuessr rejected updating the map: 500 Server Error.");

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single(m => m.directory == africa).succeeded);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);
        Assert.False(result.authInvalid);
    }

    [Fact]
    public async Task Reports_when_the_configured_maps_root_is_not_set()
    {
        using var fixture = new Fixture();
        var runner = new UpdateRunner(fixture.Vali, fixture.Geoguessr, fixture.Log,
            mapsRootProvider: () => null,
            defaultCadenceProvider: () => 7,
            nowLocalProvider: () => Now);

        var result = await runner.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.Contains("maps folder", result.preflightError);
    }
}
