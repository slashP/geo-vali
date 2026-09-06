using System.Globalization;
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class MapMetadataStoreTests
{
    private const string RealWorldMetadata = """
        {
          "id": "60a19170089ecc0001db6609",
          "name": "An Arbitrary Africa",
          "description": "Explore Africa with {{LocationCount}} locations.",
          "avatar": {
            "background": "sunrise",
            "landscape": "desserthills",
            "ground": "darkbrown",
            "decoration": "smalltrees"
          },
          "published": true,
          "updateFrequencyDays": null
        }
        """;

    [Fact]
    public async Task Reads_a_geoguessr_json_written_by_hand()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Metadata(dir), RealWorldMetadata);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.NotNull(metadata);
        Assert.Equal("60a19170089ecc0001db6609", metadata!.id);
        Assert.Equal("An Arbitrary Africa", metadata.name);
        Assert.Equal("Explore Africa with {{LocationCount}} locations.", metadata.description);
        Assert.True(metadata.published);
        Assert.Null(metadata.updateFrequencyDays);
        Assert.Equal("sunrise", metadata.avatar!.background);
        Assert.Equal("desserthills", metadata.avatar.landscape);
    }

    [Fact]
    public async Task Tolerates_extra_fields_from_the_authors_older_pipeline()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("legacy");
        await File.WriteAllTextAsync(MapPaths.Metadata(dir), """
            { "mapDistributionLink": null, "id": "abc", "highlighted": true,
              "name": "Legacy", "description": "d", "published": true,
              "shouldIncludeExtremities": false, "updateFrequencyDays": 10 }
            """);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.Equal("abc", metadata!.id);
        Assert.Equal(10, metadata.updateFrequencyDays);
    }

    [Fact]
    public async Task Returns_null_when_the_map_is_not_set_up_yet()
    {
        using var temp = new TempDir();
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(temp.Dir("brand-new")));
    }

    [Fact]
    public async Task Round_trips_metadata_through_disk()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("round-trip");
        var original = new GeoguessrMetadata
        {
            id = "6a9d6c505a43d0a64f98be5c",
            name = "Coastal Sri Lanka",
            description = "{{LocationCount}} hand-picked coastal locations.",
            avatar = new MapAvatar { background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese" },
            published = true,
            updateFrequencyDays = 10
        };

        await MapMetadataStore.WriteMetadataAsync(dir, original);
        var reloaded = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.Equal(original, reloaded);
    }

    [Fact]
    public async Task Writes_metadata_indented_and_keeps_the_token_unsubstituted()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("pretty");

        await MapMetadataStore.WriteMetadataAsync(dir, new GeoguessrMetadata
        {
            name = "N", description = "{{LocationCount}} places.", published = true
        });

        var text = await File.ReadAllTextAsync(MapPaths.Metadata(dir));
        Assert.Contains("\n  \"name\": \"N\"", text.ReplaceLineEndings("\n"));
        Assert.Contains("{{LocationCount}}", text);
    }

    [Fact]
    public async Task Reads_ephemeral_state()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("stamped");
        await File.WriteAllTextAsync(MapPaths.Ephemeral(dir), """
            { "lastPublishedTimeUtc": "2026-09-05T23:18:45.6102689Z", "updateCount": 307 }
            """);

        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);

        Assert.Equal(
            DateTime.Parse(
                "2026-09-05T23:18:45.6102689Z",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            ephemeral.lastPublishedTimeUtc);
        Assert.Equal(307, ephemeral.updateCount);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task Defaults_ephemeral_state_when_the_file_is_missing_or_corrupt()
    {
        using var temp = new TempDir();

        var missing = await MapMetadataStore.ReadEphemeralAsync(temp.Dir("never-run"));
        Assert.Equal(DateTime.MinValue, missing.lastPublishedTimeUtc);
        Assert.Equal(0, missing.updateCount);

        var corruptDir = temp.Dir("corrupt");
        await File.WriteAllTextAsync(MapPaths.Ephemeral(corruptDir), "{ this is not json");
        var corrupt = await MapMetadataStore.ReadEphemeralAsync(corruptDir);
        Assert.Equal(DateTime.MinValue, corrupt.lastPublishedTimeUtc);
        Assert.Equal(0, corrupt.updateCount);
    }

    [Fact]
    public async Task Round_trips_ephemeral_state_including_the_last_error()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("errored");
        var ephemeral = new EphemeralMetadata
        {
            lastPublishedTimeUtc = new DateTime(2026, 9, 4, 22, 11, 4, DateTimeKind.Utc),
            updateCount = 37,
            lastRunUtc = new DateTime(2026, 9, 6, 3, 0, 12, DateTimeKind.Utc),
            lastError = "vali exited with code 1."
        };

        await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
        var reloaded = await MapMetadataStore.ReadEphemeralAsync(dir);

        Assert.Equal(ephemeral.lastPublishedTimeUtc, reloaded.lastPublishedTimeUtc);
        Assert.Equal(37, reloaded.updateCount);
        Assert.Equal("vali exited with code 1.", reloaded.lastError);
        Assert.Equal(ephemeral.lastRunUtc, reloaded.lastRunUtc);
    }
}
