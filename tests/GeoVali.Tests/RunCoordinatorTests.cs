using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using GeoVali.Vali;
using Xunit;

namespace GeoVali.Tests;

public class RunCoordinatorTests
{
    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    /// <summary>A vali stand-in that signals when it starts and waits for the test to release it.</summary>
    private sealed class GatedValiRunner(TaskCompletionSource release) : FakeValiRunner
    {
        private int _starts;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Starts => Volatile.Read(ref _starts);

        public override async Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)
        {
            Interlocked.Increment(ref _starts);
            Started.TrySetResult();
            await release.Task;
            return await base.GenerateAsync(mapDirectory, onOutput, ct);
        }
    }

    private static GeoguessrMetadata Metadata() => new()
    {
        id = "existing-id",
        name = "Africa",
        description = "d",
        avatar = new MapAvatar { background = "day", landscape = "hills", ground = "green", decoration = "none" },
        published = true
    };

    /// <summary>Builds a one-map workspace and a coordinator over the supplied vali stand-in.</summary>
    private static async Task<RunCoordinator> Build(TempDir temp, FakeValiRunner vali)
    {
        var dir = temp.Dir("maps", "africa");
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        await MapMetadataStore.WriteMetadataAsync(dir, Metadata());
        vali.LocationsByDirectory[dir] = FiveLocations;

        var log = new RunLog(Path.Combine(temp.Path, "logs"), () => null);
        var runner = new UpdateRunner(vali, new FakeGeoguessrClient(), log,
            () => Path.Combine(temp.Path, "maps"), () => 7, () => DateTime.Now);
        return new RunCoordinator(runner, log);
    }

    [Fact]
    public async Task Runs_and_reports_the_result()
    {
        using var temp = new TempDir();
        var coordinator = await Build(temp, new FakeValiRunner());

        var result = await coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.preflightFailed);
        Assert.True(result.maps.Single().succeeded);
        Assert.Same(result, coordinator.LastResult);
    }

    [Fact]
    public async Task Is_not_running_before_or_after_a_run()
    {
        using var temp = new TempDir();
        var coordinator = await Build(temp, new FakeValiRunner());

        Assert.False(coordinator.IsRunning);
        await coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task A_second_request_joins_the_in_flight_run_instead_of_starting_another()
    {
        using var temp = new TempDir();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vali = new GatedValiRunner(release);
        var coordinator = await Build(temp, vali);

        var first = coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);
        await vali.Started.Task; // the run is genuinely in flight

        Assert.True(coordinator.IsRunning);
        var second = coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        release.SetResult();
        var firstResult = await first;
        var secondResult = await second;

        // Joined, not queued: one generation, one shared result.
        Assert.Equal(1, vali.Starts);
        Assert.Same(firstResult, secondResult);
    }

    [Fact]
    public async Task A_run_after_the_previous_one_finished_starts_fresh()
    {
        using var temp = new TempDir();
        var vali = new FakeValiRunner();
        var coordinator = await Build(temp, vali);

        var first = await coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);
        var second = await coordinator.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.NotSame(first, second);
        Assert.Equal(2, vali.GeneratedDirectories.Count);
    }

    [Fact]
    public async Task Exposes_the_next_scheduled_run_for_the_dashboard_header()
    {
        using var temp = new TempDir();
        var coordinator = await Build(temp, new FakeValiRunner());

        var next = DateTime.UtcNow.AddMinutes(30);
        coordinator.NextScheduledRunUtc = next;

        Assert.Equal(next, coordinator.NextScheduledRunUtc);
    }
}
