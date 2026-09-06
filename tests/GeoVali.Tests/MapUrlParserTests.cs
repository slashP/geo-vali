using GeoVali.Geoguessr;
using Xunit;

namespace GeoVali.Tests;

public class MapUrlParserTests
{
    [Theory]
    [InlineData("https://www.geoguessr.com/maps/60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    [InlineData("https://www.geoguessr.com/maps/60a19170089ecc0001db6609/play", "60a19170089ecc0001db6609")]
    [InlineData("https://www.geoguessr.com/map-maker/60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    [InlineData("http://geoguessr.com/maps/60a19170089ecc0001db6609?x=1", "60a19170089ecc0001db6609")]
    [InlineData("  https://www.geoguessr.com/maps/60a19170089ecc0001db6609  ", "60a19170089ecc0001db6609")]
    [InlineData("60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    public void Extracts_the_map_id(string input, string expected)
    {
        Assert.True(MapUrlParser.TryExtractMapId(input, out var mapId));
        Assert.Equal(expected, mapId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.geoguessr.com/")]
    [InlineData("https://www.geoguessr.com/maps/")]
    [InlineData("not a url at all")]
    [InlineData("https://example.com/maps/60a19170089ecc0001db6609")]
    public void Rejects_input_that_is_not_a_geoguessr_map(string input)
    {
        Assert.False(MapUrlParser.TryExtractMapId(input, out var mapId));
        Assert.Equal(string.Empty, mapId);
    }
}
