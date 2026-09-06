using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GeoVali;
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using GeoVali.Vali;
using Xunit;

namespace GeoVali.Tests;

public class ApiEndpointTests : IDisposable
{
    private readonly TempDir _temp = new();

    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private FakeValiRunner Vali { get; } = new();
    private FakeGeoguessrClient Geoguessr { get; } = new();

    private string MapsRoot => Path.Combine(_temp.Path, "maps");

    /// <summary>Hosts the real app with the two outside-world dependencies replaced.</summary>
    private HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValiRunner>();
                services.RemoveAll<IGeoguessrClient>();
                services.RemoveAll<IHostedService>();   // no background scheduler in tests
                services.AddSingleton<IValiRunner>(Vali);
                services.AddSingleton<IGeoguessrClient>(Geoguessr);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-GeoVali", "1");
        return client;
    }

    private async Task<string> AddConfiguredMap(string folderName, string name, int? cadence = null, EphemeralMetadata? ephemeral = null)
    {
        var dir = Path.Combine(MapsRoot, folderName);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        await MapMetadataStore.WriteMetadataAsync(dir, new GeoguessrMetadata
        {
            id = $"{folderName}-id",
            name = name,
            description = "{{LocationCount}} locations.",
            avatar = new MapAvatar { background = "day", landscape = "hills", ground = "green", decoration = "none" },
            published = true,
            updateFrequencyDays = cadence
        });
        if (ephemeral is not null)
        {
            await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
        }

        Vali.LocationsByDirectory[dir] = FiveLocations;
        return dir;
    }

    private void ConfigureMapsRoot()
    {
        Directory.CreateDirectory(MapsRoot);
        new ConfigStore(_temp.Path).Write(new AppConfig { mapsRoot = MapsRoot });
        new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).WriteCookie("test-cookie");
    }

    [Fact]
    public async Task Status_reports_setup_incomplete_before_a_maps_folder_is_chosen()
    {
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["setupComplete"]!.GetValue<bool>());
        Assert.Null(status["mapsRoot"]?.GetValue<string>());
        Assert.Equal(ValiRunner.InstallCommand, status["valiInstallCommand"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ping_identifies_the_tool_without_calling_geoguessr()
    {
        using var client = CreateClient();

        var ping = await client.GetFromJsonAsync<JsonNode>("/api/ping");

        Assert.Equal(AppInfo.ProductName, ping!["product"]!.GetValue<string>());
        Assert.Equal(AppInfo.Version, ping["version"]!.GetValue<string>());
        Assert.Equal(0, Geoguessr.AuthProbeCount);
    }

    [Fact]
    public async Task Status_reports_the_signed_in_user_and_the_maps_folder_once_set_up()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.True(status!["setupComplete"]!.GetValue<bool>());
        Assert.Equal(MapsRoot, status["mapsRoot"]!.GetValue<string>());
        Assert.True(status["authValid"]!.GetValue<bool>());
        Assert.Equal("slashP", status["userNick"]!.GetValue<string>());
        Assert.True(status["valiInstalled"]!.GetValue<bool>());
        Assert.False(status["running"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Status_reports_an_expired_cookie()
    {
        ConfigureMapsRoot();
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["authValid"]!.GetValue<bool>());
        Assert.Null(status["userNick"]?.GetValue<string>());
    }

    [Fact]
    public async Task Status_reports_that_vali_is_missing()
    {
        ConfigureMapsRoot();
        Vali.Executable = null;
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["valiInstalled"]!.GetValue<bool>());
        Assert.Equal("dotnet tool install -g vali", status["valiInstallCommand"]!.GetValue<string>());
    }

    [Fact]
    public async Task Maps_lists_configured_maps_with_their_state()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "An Arbitrary Africa", cadence: 10, ephemeral: new EphemeralMetadata
        {
            lastPublishedTimeUtc = DateTime.UtcNow.AddDays(-20),
            updateCount = 307,
            lastError = "vali exited with code 1."
        });
        using var client = CreateClient();

        var maps = await client.GetFromJsonAsync<JsonNode>("/api/maps");

        var row = maps!.AsArray().Single();
        Assert.Equal("An Arbitrary Africa", row!["name"]!.GetValue<string>());
        Assert.True(row["configured"]!.GetValue<bool>());
        Assert.Equal(10, row["cadenceDays"]!.GetValue<int>());
        Assert.Equal(307, row["updateCount"]!.GetValue<int>());
        Assert.Equal("vali exited with code 1.", row["lastError"]!.GetValue<string>());
        Assert.True(row["due"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Maps_lists_a_folder_without_geoguessr_json_as_not_set_up()
    {
        ConfigureMapsRoot();
        var dir = Path.Combine(MapsRoot, "brand-new");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        using var client = CreateClient();

        var row = (await client.GetFromJsonAsync<JsonNode>("/api/maps"))!.AsArray().Single();

        Assert.False(row!["configured"]!.GetValue<bool>());
        Assert.Equal("brand-new", row["folderName"]!.GetValue<string>());
        Assert.Equal("brand-new", row["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Maps_uses_the_global_default_cadence_when_the_map_sets_none()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa", cadence: null);
        using var client = CreateClient();

        var row = (await client.GetFromJsonAsync<JsonNode>("/api/maps"))!.AsArray().Single();

        Assert.Equal(7, row!["cadenceDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task Maps_returns_an_empty_list_before_setup()
    {
        using var client = CreateClient();

        var maps = await client.GetFromJsonAsync<JsonNode>("/api/maps");

        Assert.Empty(maps!.AsArray());
    }

    [Fact]
    public async Task A_mutating_call_without_the_geovali_header_is_refused()
    {
        ConfigureMapsRoot();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValiRunner>();
                services.RemoveAll<IGeoguessrClient>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IValiRunner>(Vali);
                services.AddSingleton<IGeoguessrClient>(Geoguessr);
            });
        });
        using var client = factory.CreateClient(); // no X-GeoVali header

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "all" });

        // A page on another origin cannot set a custom header without a preflight we never answer.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_read_only_call_without_the_header_still_works()
    {
        using var client = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
                builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
            })
            .CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Run_all_regenerates_and_publishes_every_configured_map()
    {
        ConfigureMapsRoot();
        var africa = await AddConfiguredMap("africa", "Africa");
        var europe = await AddConfiguredMap("europe", "Europe");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "all" });
        var result = await response.Content.ReadFromJsonAsync<JsonNode>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!["preflightFailed"]!.GetValue<bool>());
        Assert.Equal(2, result["maps"]!.AsArray().Count);
        Assert.Equal([africa, europe], Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_due_skips_a_map_published_today()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa", cadence: 10,
            ephemeral: new EphemeralMetadata { lastPublishedTimeUtc = DateTime.UtcNow, updateCount = 1 });
        using var client = CreateClient();

        await client.PostAsJsonAsync("/api/run", new { scope = "due" });

        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_single_regenerates_only_the_named_map()
    {
        ConfigureMapsRoot();
        var africa = await AddConfiguredMap("africa", "Africa");
        await AddConfiguredMap("europe", "Europe");
        using var client = CreateClient();

        await client.PostAsJsonAsync("/api/run", new { scope = "single", directory = africa });

        Assert.Equal([africa], Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_single_without_a_directory_is_a_bad_request()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "single" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_rejects_a_directory_outside_the_maps_folder()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run",
            new { scope = "single", directory = Path.Combine(_temp.Path, "somewhere-else") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_reports_a_preflight_abort_when_the_cookie_is_dead()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa");
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var result = await (await client.PostAsJsonAsync("/api/run", new { scope = "all" }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(result!["preflightFailed"]!.GetValue<bool>());
        Assert.True(result["authInvalid"]!.GetValue<bool>());
        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Events_streams_log_lines_from_a_run()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "An Arbitrary Africa");
        using var client = CreateClient();

        using var stream = await client.GetStreamAsync("/api/events");
        using var reader = new StreamReader(stream);

        await client.PostAsJsonAsync("/api/run", new { scope = "all" });

        var seen = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            seen.Add(line);
            if (line.Contains("An Arbitrary Africa"))
            {
                break;
            }
        }

        Assert.Contains(seen, line => line.StartsWith("data:") && line.Contains("An Arbitrary Africa"));
    }

    [Fact]
    public async Task Browse_lists_folders_and_flags_the_ones_holding_maps()
    {
        Directory.CreateDirectory(Path.Combine(MapsRoot, "africa"));
        await File.WriteAllTextAsync(Path.Combine(MapsRoot, "africa", MapPaths.DefinitionFileName), "{}");
        using var client = CreateClient();

        var listing = await client.GetFromJsonAsync<JsonNode>($"/api/browse?path={Uri.EscapeDataString(MapsRoot)}");

        var entry = listing!["entries"]!.AsArray().Single();
        Assert.Equal("africa", entry!["name"]!.GetValue<string>());
        Assert.True(entry["hasMapJson"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Choosing_a_maps_folder_saves_it_and_reports_how_many_maps_are_in_it()
    {
        Directory.CreateDirectory(Path.Combine(MapsRoot, "africa"));
        await File.WriteAllTextAsync(Path.Combine(MapsRoot, "africa", MapPaths.DefinitionFileName), "{}");
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/setup/folder", new { path = MapsRoot }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal(1, body["mapCount"]!.GetValue<int>());
        Assert.Equal(MapsRoot, new ConfigStore(_temp.Path).Read().mapsRoot);
    }

    [Fact]
    public async Task Choosing_a_folder_that_does_not_exist_is_refused_with_a_readable_message()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/folder",
            new { path = Path.Combine(_temp.Path, "nope") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.Contains("does not exist", body!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_valid_cookie_is_saved_and_the_signed_in_name_confirmed()
    {
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "fresh-ncfa-value" }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal("slashP", body["nick"]!.GetValue<string>());
        Assert.Equal("fresh-ncfa-value",
            new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }

    [Fact]
    public async Task A_rejected_cookie_is_not_saved()
    {
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "stale-value" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }

    [Fact]
    public async Task An_empty_cookie_is_refused()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_cookie_is_never_echoed_back_in_a_response()
    {
        using var client = CreateClient();
        await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "fresh-ncfa-value" });

        var status = await (await client.GetAsync("/api/status")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("fresh-ncfa-value", status);
    }

    private async Task<string> AddUnconfiguredMap(string folderName)
    {
        var dir = Path.Combine(MapsRoot, folderName);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        Vali.LocationsByDirectory[dir] = FiveLocations;
        return dir;
    }

    [Fact]
    public async Task Creating_a_map_writes_geoguessr_json_with_a_generated_avatar_and_no_id()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("coastal");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create", new
        {
            directory = dir,
            name = "Coastal Sri Lanka",
            description = "{{LocationCount}} hand-picked coastal locations."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("Coastal Sri Lanka", metadata!.name);
        Assert.Equal("{{LocationCount}} hand-picked coastal locations.", metadata.description);
        Assert.Null(metadata.id);          // filled in on the first publish
        Assert.True(metadata.published);
        Assert.NotNull(metadata.avatar);
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar!.background));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.landscape));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.ground));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.decoration));
    }

    [Fact]
    public async Task Creating_a_map_refuses_a_folder_that_is_already_set_up()
    {
        ConfigureMapsRoot();
        var dir = await AddConfiguredMap("africa", "Africa");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = dir, name = "Africa Again", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Africa", (await MapMetadataStore.ReadMetadataAsync(dir))!.name);
    }

    [Fact]
    public async Task Creating_a_map_refuses_a_folder_outside_the_maps_root()
    {
        ConfigureMapsRoot();
        var outside = Path.Combine(_temp.Path, "elsewhere");
        Directory.CreateDirectory(outside);
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = outside, name = "Sneaky", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_map_refuses_an_empty_name()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("coastal");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = dir, name = "  ", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Linking_an_existing_map_seeds_geoguessr_json_from_the_draft()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        Geoguessr.Drafts["60a19170089ecc0001db6609"] =
            new DraftInfo("60a19170089ecc0001db6609", "An Arbitrary Africa", 306);
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/maps/link", new
        {
            directory = dir,
            url = "https://www.geoguessr.com/maps/60a19170089ecc0001db6609"
        })).Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal("An Arbitrary Africa", body["name"]!.GetValue<string>());

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("60a19170089ecc0001db6609", metadata!.id);
        Assert.Equal("An Arbitrary Africa", metadata.name);
        Assert.NotNull(metadata.avatar);
    }

    [Fact]
    public async Task Linking_refuses_a_url_that_is_not_a_geoguessr_map()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/link",
            new { directory = dir, url = "https://example.com/whatever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(dir));
    }

    [Fact]
    public async Task Linking_refuses_a_map_the_signed_in_user_does_not_own()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        using var client = CreateClient();

        // No draft registered, so GetDraftAsync returns null.
        var response = await client.PostAsJsonAsync("/api/maps/link", new
        {
            directory = dir,
            url = "https://www.geoguessr.com/maps/60a19170089ecc0001db6609"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.Contains("could not be found on your account", body!["error"]!.GetValue<string>());
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(dir));
    }

    [Fact]
    public async Task Settings_returns_the_current_values()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var settings = await client.GetFromJsonAsync<JsonNode>("/api/settings");

        Assert.Equal(MapsRoot, settings!["mapsRoot"]!.GetValue<string>());
        Assert.Equal(7, settings["defaultCadenceDays"]!.GetValue<int>());
        Assert.Equal(30, settings["checkIntervalMinutes"]!.GetValue<int>());
        Assert.Equal(5099, settings["dashboardPort"]!.GetValue<int>());
        Assert.False(settings["startAtLogin"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(settings["credentialProtectionNote"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Settings_saves_changed_values()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new
        {
            defaultCadenceDays = 10,
            checkIntervalMinutes = 60
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = new ConfigStore(_temp.Path).Read();
        Assert.Equal(10, saved.defaultCadenceDays);
        Assert.Equal(60, saved.checkIntervalMinutes);
        Assert.Equal(MapsRoot, saved.mapsRoot); // untouched fields survive
    }

    [Fact]
    public async Task Settings_refuses_a_cadence_below_one_day()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new { defaultCadenceDays = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(7, new ConfigStore(_temp.Path).Read().defaultCadenceDays);
    }

    [Fact]
    public async Task Settings_refuses_a_check_interval_below_one_minute()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new { checkIntervalMinutes = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_refuses_a_maps_root_that_does_not_exist()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings",
            new { mapsRoot = Path.Combine(_temp.Path, "nope") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_port_reports_that_a_restart_is_needed()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/settings", new { dashboardPort = 5150 }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["restartRequired"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Clearing_the_cookie_removes_it()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        await client.PostAsync("/api/settings/cookie/clear", null);

        Assert.Null(new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }

    [Fact]
    public async Task The_dashboard_page_is_served_from_the_embedded_resources()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("GeoVali", html);
        Assert.Contains("app.js", html);
    }

    [Fact]
    public async Task The_dashboard_script_and_stylesheet_are_served_too()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/style.css")).StatusCode);
    }

    public void Dispose() => _temp.Dispose();
}
