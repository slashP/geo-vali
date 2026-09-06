using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class LocationFileTests
{
    // Trimmed from a real vali map-locations.json: a bare array, extra fields that
    // GeoVali does not care about, and no "pitch" property at all.
    private const string ValiOutput = """
        [{"lat":-19.36172016699375,"lng":25.87176560604501,"heading":153,"extra":{"tags":["2012"]},
          "panoId":"rJ_gIkM2XxRPedayIlzlBw","countryCode":"BW","subdivisionCode":"BW-CE",
          "locationId":"3870162097","resolutionHeight":6656,"year":2012,"month":4},
         {"lat":-32.43315093026216,"lng":24.627465821100692,"heading":341,
          "panoId":"35_3U7bbl8IW_05SV2WmCA","countryCode":"ZA"}]
        """;

    [Fact]
    public async Task Reads_the_fields_geoguessr_needs_and_ignores_the_rest()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), ValiOutput);

        var locations = await LocationFile.ReadAsync(dir, CancellationToken.None);

        Assert.Equal(2, locations.Count);
        Assert.Equal(-19.36172016699375, locations[0].lat);
        Assert.Equal(25.87176560604501, locations[0].lng);
        Assert.Equal(153, locations[0].heading);
        Assert.Equal("rJ_gIkM2XxRPedayIlzlBw", locations[0].panoId);
    }

    [Fact]
    public async Task Defaults_pitch_to_zero_because_vali_does_not_write_it()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), ValiOutput);

        var locations = await LocationFile.ReadAsync(dir, CancellationToken.None);

        Assert.All(locations, location => Assert.Equal(0, location.pitch));
    }

    [Fact]
    public async Task Returns_empty_when_vali_produced_no_file()
    {
        using var temp = new TempDir();
        var locations = await LocationFile.ReadAsync(temp.Dir("never-generated"), CancellationToken.None);
        Assert.Empty(locations);
    }

    [Fact]
    public async Task Reports_a_corrupt_file_as_a_readable_error()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("truncated");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), "[{\"lat\":1,\"lng\":2");

        var exception = await Assert.ThrowsAsync<NotPublishableException>(
            () => LocationFile.ReadAsync(dir, CancellationToken.None));

        Assert.Contains("map-locations.json", exception.Message);
        Assert.Contains("could not be read", exception.Message);
    }
}
