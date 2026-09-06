using GeoVali.Maps;
using GeoVali.Vali;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class ValiRunnerTests
{
    private const string TwoLocations =
        """[{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"}]""";

    [Fact]
    public async Task Runs_the_executable_and_writes_locations_beside_the_definition()
    {
        using var temp = new TempDir();
        var fake = FakeValiExecutable.Create(temp, TwoLocations);
        var mapDir = temp.Dir("maps", "coastal");
        await File.WriteAllTextAsync(MapPaths.Definition(mapDir), "{}");

        var result = await new ValiRunner(fake).GenerateAsync(mapDir, _ => { }, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(MapPaths.Locations(mapDir)));
        Assert.Equal(TwoLocations, (await File.ReadAllTextAsync(MapPaths.Locations(mapDir))).Trim());
    }

    [Fact]
    public async Task Streams_stdout_line_by_line_to_the_callback()
    {
        using var temp = new TempDir();
        var fake = FakeValiExecutable.Create(temp, TwoLocations, stdout: "Finding locations in Botswana");
        var mapDir = temp.Dir("maps", "botswana");
        await File.WriteAllTextAsync(MapPaths.Definition(mapDir), "{}");

        var lines = new List<string>();
        await new ValiRunner(fake).GenerateAsync(mapDir, lines.Add, CancellationToken.None);

        Assert.Contains("Finding locations in Botswana", lines);
    }

    [Fact]
    public async Task Surfaces_a_non_zero_exit_code_and_the_last_line_of_output()
    {
        using var temp = new TempDir();
        var fake = FakeValiExecutable.Create(temp, TwoLocations, exitCode: 1, stdout: "Country code XX is not valid.");
        var mapDir = temp.Dir("maps", "broken");
        await File.WriteAllTextAsync(MapPaths.Definition(mapDir), "{}");

        var result = await new ValiRunner(fake).GenerateAsync(mapDir, _ => { }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Country code XX is not valid.", result.LastOutputLine);
    }

    [Fact]
    public async Task Reports_a_missing_executable_with_the_install_command()
    {
        using var temp = new TempDir();
        var mapDir = temp.Dir("maps", "any");
        await File.WriteAllTextAsync(MapPaths.Definition(mapDir), "{}");
        var runner = new ValiRunner(Path.Combine(temp.Path, "definitely-not-here"));

        var exception = await Assert.ThrowsAsync<ValiNotFoundException>(
            () => runner.GenerateAsync(mapDir, _ => { }, CancellationToken.None));

        Assert.Contains("dotnet tool install -g vali", exception.Message);
    }

    [Fact]
    public void Finds_an_explicitly_configured_executable()
    {
        using var temp = new TempDir();
        var fake = FakeValiExecutable.Create(temp, "[]");
        Assert.Equal(fake, new ValiRunner(fake).FindExecutable());
    }

    [Fact]
    public void Reports_no_executable_when_the_configured_path_does_not_exist()
    {
        Assert.Null(new ValiRunner(Path.Combine(Path.GetTempPath(), "no-vali-" + Guid.NewGuid())).FindExecutable());
    }

    [Theory]
    // Real vali output: an SGR colour sequence wrapping each banner line.
    [InlineData("\u001b[38;5;12m888  888  8888b.  888 888\u001b[0m", "888  888  8888b.  888 888")]
    [InlineData("\u001b[38;5;2mDownload/data folder: /data/Vali\u001b[0m", "Download/data folder: /data/Vali")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    public void Strips_the_ansi_colour_codes_vali_prints(string input, string expected)
    {
        Assert.Equal(expected, ValiRunner.StripAnsi(input));
    }

    [Fact]
    public void Install_command_is_the_one_the_user_should_run()
    {
        Assert.Equal("dotnet tool install -g vali", ValiRunner.InstallCommand);
    }
}
