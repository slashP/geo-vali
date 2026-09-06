using GeoVali;
using Xunit;

namespace GeoVali.Tests;

public class AppInfoTests
{
    [Fact]
    public void Reports_product_name_and_a_non_empty_version()
    {
        Assert.Equal("GeoVali", AppInfo.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.NotEqual("0.0.0", AppInfo.Version);
    }
}
