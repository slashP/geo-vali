using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class DescriptionTemplateTests
{
    [Fact]
    public void Replaces_the_location_count_token_with_the_number()
    {
        Assert.Equal(
            "1234 hand-picked coastal locations.",
            DescriptionTemplate.Expand("{{LocationCount}} hand-picked coastal locations.", 1234));
    }

    [Fact]
    public void Replaces_every_occurrence()
    {
        Assert.Equal(
            "7 in, 7 out",
            DescriptionTemplate.Expand("{{LocationCount}} in, {{LocationCount}} out", 7));
    }

    [Fact]
    public void Formats_the_count_invariantly_with_no_thousands_separator()
    {
        Assert.Equal("55000 locations", DescriptionTemplate.Expand("{{LocationCount}} locations", 55000));
    }

    [Fact]
    public void Leaves_a_description_without_the_token_untouched()
    {
        Assert.Equal("Just a map.", DescriptionTemplate.Expand("Just a map.", 42));
    }

    [Fact]
    public void Handles_a_null_or_empty_description()
    {
        Assert.Equal(string.Empty, DescriptionTemplate.Expand(null!, 42));
        Assert.Equal(string.Empty, DescriptionTemplate.Expand("", 42));
    }
}
