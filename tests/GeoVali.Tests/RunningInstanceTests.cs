using System.Diagnostics;
using GeoVali.Startup;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class RunningInstanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Finds_a_geovali_already_listening_on_the_port()
    {
        await using var owner = await StubPortOwner.StartAsync("""{"product":"GeoVali","version":"9.9.9"}""");

        var url = await RunningInstance.FindAsync(owner.Port, Timeout);

        Assert.Equal($"http://127.0.0.1:{owner.Port}", url);
    }

    [Fact]
    public async Task Ignores_a_port_owned_by_another_program()
    {
        await using var owner = await StubPortOwner.StartAsync("""{"product":"SomethingElse"}""");

        Assert.Null(await RunningInstance.FindAsync(owner.Port, Timeout));
    }

    [Fact]
    public async Task Ignores_a_port_that_answers_but_not_with_json()
    {
        await using var owner = await StubPortOwner.StartAsync("<html>Router admin</html>");

        Assert.Null(await RunningInstance.FindAsync(owner.Port, Timeout));
    }

    [Fact]
    public async Task Reports_nothing_when_the_port_is_free()
    {
        var free = PortPicker.FindFree(45871);

        Assert.Null(await RunningInstance.FindAsync(free, Timeout));
    }

    [Fact]
    public async Task Gives_up_on_a_port_that_accepts_but_never_answers()
    {
        await using var owner = await StubPortOwner.StartAsync(
            """{"product":"GeoVali"}""", delay: TimeSpan.FromSeconds(30));

        var clock = Stopwatch.StartNew();
        var url = await RunningInstance.FindAsync(owner.Port, Timeout);
        clock.Stop();

        Assert.Null(url);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"probe took {clock.Elapsed}");
    }
}
