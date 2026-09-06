using GeoVali.Maps;
using GeoVali.Tests.Support;
using GeoVali.Web;
using Xunit;

namespace GeoVali.Tests;

public class FolderBrowserTests
{
    [Fact]
    public void Lists_subdirectories_of_a_path()
    {
        using var temp = new TempDir();
        temp.Dir("africa");
        temp.Dir("europe");
        temp.File("notes.txt", "not a directory");

        var listing = FolderBrowser.List(temp.Path);

        Assert.Equal(["africa", "europe"], listing.entries.Select(e => e.name));
        Assert.Equal(temp.Path, listing.path);
    }

    [Fact]
    public void Flags_a_directory_that_is_itself_a_map()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("africa", MapPaths.DefinitionFileName), "{}");
        temp.Dir("not-a-map");

        var listing = FolderBrowser.List(temp.Path);

        Assert.True(listing.entries.Single(e => e.name == "africa").hasMapJson);
        Assert.False(listing.entries.Single(e => e.name == "not-a-map").hasMapJson);
    }

    [Fact]
    public void Counts_the_maps_below_each_directory_so_the_user_can_see_which_to_pick()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("map-definitions", "africa", MapPaths.DefinitionFileName), "{}");
        temp.File(Path.Combine("map-definitions", "europe", MapPaths.DefinitionFileName), "{}");
        temp.Dir("photos");

        var listing = FolderBrowser.List(temp.Path);

        Assert.Equal(2, listing.entries.Single(e => e.name == "map-definitions").mapCountBelow);
        Assert.Equal(0, listing.entries.Single(e => e.name == "photos").mapCountBelow);
    }

    [Fact]
    public void Reports_the_parent_so_the_user_can_navigate_up()
    {
        using var temp = new TempDir();
        var child = temp.Dir("child");

        Assert.Equal(temp.Path, FolderBrowser.List(child).parent);
    }

    [Fact]
    public void Lists_filesystem_roots_when_no_path_is_given()
    {
        var listing = FolderBrowser.List(null);

        Assert.Null(listing.path);
        Assert.NotEmpty(listing.entries);
    }

    [Fact]
    public void Returns_the_roots_listing_for_a_path_that_does_not_exist()
    {
        var listing = FolderBrowser.List(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()));

        Assert.Null(listing.path);
        Assert.NotEmpty(listing.entries);
    }

    [Fact]
    public void Skips_directories_it_cannot_read_rather_than_failing_the_listing()
    {
        using var temp = new TempDir();
        temp.Dir("readable");

        // Whatever the OS denies, the listing must still come back.
        var listing = FolderBrowser.List(temp.Path);

        Assert.Contains(listing.entries, e => e.name == "readable");
    }
}
