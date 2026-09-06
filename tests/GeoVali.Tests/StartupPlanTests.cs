using GeoVali.Startup;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class StartupPlanTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Joins_the_geovali_that_already_owns_the_port()
    {
        await using var owner = await StubPortOwner.StartAsync("""{"product":"GeoVali","version":"9.9.9"}""");

        var plan = await StartupPlan.CreateAsync(owner.Port, Timeout);

        Assert.True(plan.AlreadyRunning);
        Assert.Equal($"http://127.0.0.1:{owner.Port}", plan.Url);
    }

    [Fact]
    public async Task Takes_the_configured_port_when_nothing_holds_it()
    {
        var free = PortPicker.FindFree(45921);

        var plan = await StartupPlan.CreateAsync(free, Timeout);

        Assert.False(plan.AlreadyRunning);
        Assert.Equal(free, plan.Port);
        Assert.Equal($"http://127.0.0.1:{free}", plan.Url);
    }

    [Fact]
    public async Task Steps_past_a_port_held_by_another_program_instead_of_joining_it()
    {
        await using var owner = await StubPortOwner.StartAsync("""{"product":"SomethingElse"}""");

        var plan = await StartupPlan.CreateAsync(owner.Port, Timeout);

        Assert.False(plan.AlreadyRunning);
        Assert.True(plan.Port > owner.Port, $"expected a port above {owner.Port}, got {plan.Port}");
    }
}
