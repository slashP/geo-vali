using GeoVali.Configuration;
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Returns_documented_defaults_when_there_is_no_config_file()
    {
        using var temp = new TempDir();

        var config = new ConfigStore(temp.Path).Read();

        Assert.Null(config.mapsRoot);
        Assert.Equal(Cadence.DefaultDaysBetweenUpdates, config.defaultCadenceDays);
        Assert.Equal(7, config.defaultCadenceDays);
        Assert.Equal(30, config.checkIntervalMinutes);
        Assert.Equal(5099, config.dashboardPort);
        Assert.False(config.startAtLogin);
        Assert.Null(config.valiExecutablePath);
    }

    [Fact]
    public void Round_trips_config_through_disk()
    {
        using var temp = new TempDir();
        var config = new AppConfig
        {
            mapsRoot = Path.Combine(temp.Path, "map-definitions"),
            defaultCadenceDays = 10,
            checkIntervalMinutes = 60,
            dashboardPort = 5100,
            startAtLogin = true,
            valiExecutablePath = Path.Combine(temp.Path, "vali")
        };

        new ConfigStore(temp.Path).Write(config);

        Assert.Equal(config, new ConfigStore(temp.Path).Read());
    }

    [Fact]
    public void Write_updates_the_cached_current_config()
    {
        using var temp = new TempDir();
        var store = new ConfigStore(temp.Path);

        store.Write(store.Current with { defaultCadenceDays = 3 });

        Assert.Equal(3, store.Current.defaultCadenceDays);
    }

    [Fact]
    public void Falls_back_to_defaults_when_the_file_is_corrupt()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, ConfigStore.FileName), "{ not json");

        // A hand-edited config that no longer parses must not stop the tool from starting.
        Assert.Equal(7, new ConfigStore(temp.Path).Read().defaultCadenceDays);
    }

    [Fact]
    public void Creates_the_config_directory_on_write()
    {
        using var temp = new TempDir();
        var nested = Path.Combine(temp.Path, "does", "not", "exist");

        new ConfigStore(nested).Write(new AppConfig());

        Assert.True(File.Exists(Path.Combine(nested, ConfigStore.FileName)));
    }

    [Fact]
    public void Config_directory_is_a_rooted_platform_location()
    {
        var directory = AppPaths.ConfigDirectory;

        Assert.False(string.IsNullOrWhiteSpace(directory));
        Assert.True(Path.IsPathRooted(directory));
        Assert.EndsWith(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? "GeoVali" : "geovali",
            directory);
        Assert.Equal(Path.Combine(directory, "config.json"), AppPaths.ConfigFile);
        Assert.Equal(Path.Combine(directory, "credentials.json"), AppPaths.CredentialsFile);
    }
}
