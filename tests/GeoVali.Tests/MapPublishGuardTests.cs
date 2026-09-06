using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class MapPublishGuardTests
{
    [Fact]
    public void Minimum_is_five_locations()
    {
        Assert.Equal(5, MapPublishGuard.MinimumLocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Refuses_to_publish_a_map_with_too_few_locations(int count)
    {
        var exception = Assert.Throws<NotPublishableException>(
            () => MapPublishGuard.EnsurePublishable("Coastal Sri Lanka", count));

        // GeoGuessr answers a bare 400 here, which is unreadable in a summary.
        // Say what is actually wrong instead.
        Assert.Contains("Coastal Sri Lanka", exception.Message);
        Assert.Contains(count.ToString(), exception.Message);
        Assert.Contains("at least 5", exception.Message);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(55000)]
    public void Allows_a_map_with_enough_locations(int count)
    {
        MapPublishGuard.EnsurePublishable("An Arbitrary Africa", count);
    }
}
