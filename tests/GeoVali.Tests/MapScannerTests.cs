using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class MapScannerTests
{
    [Fact]
    public void Finds_every_directory_containing_map_json_at_any_depth()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("arbitrary", "AFRICA", "map.json"), "{}");
        temp.File(Path.Combine("arbitrary", "EUROPE", "map.json"), "{}");
        temp.File(Path.Combine("one-offs", "deep", "deeper", "narnia", "map.json"), "{}");

        var found = MapScanner.Scan(temp.Path);

        Assert.Equal(3, found.Count);
        Assert.Contains(found, m => m.FolderName == "AFRICA");
        Assert.Contains(found, m => m.FolderName == "EUROPE");
        Assert.Contains(found, m => m.FolderName == "narnia");
    }

    [Fact]
    public void Classifies_a_folder_with_geoguessr_json_as_configured()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("set-up", "map.json"), "{}");
        temp.File(Path.Combine("set-up", "geoguessr.json"), "{}");
        temp.File(Path.Combine("brand-new", "map.json"), "{}");

        var found = MapScanner.Scan(temp.Path);

        Assert.True(found.Single(m => m.FolderName == "set-up").IsConfigured);
        Assert.False(found.Single(m => m.FolderName == "brand-new").IsConfigured);
    }

    [Fact]
    public void Ignores_directories_without_a_map_json()
    {
        using var temp = new TempDir();
        temp.Dir("empty");
        temp.File(Path.Combine("not-a-map", "notes.txt"), "hello");
        temp.File(Path.Combine("real", "map.json"), "{}");

        var found = MapScanner.Scan(temp.Path);

        Assert.Single(found);
        Assert.Equal("real", found[0].FolderName);
    }

    [Fact]
    public void Treats_the_root_itself_as_a_map_when_it_holds_a_map_json()
    {
        using var temp = new TempDir();
        temp.File("map.json", "{}");

        var found = MapScanner.Scan(temp.Path);

        Assert.Single(found);
        Assert.Equal(temp.Path, found[0].Directory);
    }

    [Fact]
    public void Returns_results_in_a_stable_order()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("zulu", "map.json"), "{}");
        temp.File(Path.Combine("alpha", "map.json"), "{}");
        temp.File(Path.Combine("mike", "map.json"), "{}");

        var first = MapScanner.Scan(temp.Path).Select(m => m.FolderName).ToArray();
        var second = MapScanner.Scan(temp.Path).Select(m => m.FolderName).ToArray();

        Assert.Equal(new[] { "alpha", "mike", "zulu" }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Returns_nothing_for_a_root_that_does_not_exist()
    {
        var found = MapScanner.Scan(Path.Combine(Path.GetTempPath(), "geovali-no-such-folder-" + Guid.NewGuid()));
        Assert.Empty(found);
    }
}
