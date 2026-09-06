namespace GeoVali.Running;

/// <summary>
/// The single run-lock. A manual "Run now" during an active run joins the in-flight run rather
/// than starting a second, so there is never more than one vali process at a time.
/// </summary>
public sealed class RunCoordinator(UpdateRunner runner, RunLog log)
{
    private readonly Lock _gate = new();
    private Task<RunResult>? _current;

    public bool IsRunning => _current is { IsCompleted: false };

    /// <summary>The map the run is on right now, for the dashboard's live panel.</summary>
    public string? CurrentMapName { get; set; }

    /// <summary>Set by the scheduler so the dashboard header can show the next run.</summary>
    public DateTime? NextScheduledRunUtc { get; set; }

    public RunResult? LastResult { get; private set; }

    public Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_current is { IsCompleted: false } inFlight)
            {
                log.Info("A run is already in progress; joining it.");
                return inFlight;
            }

            _current = Execute(request, ct);
            return _current;
        }
    }

    private async Task<RunResult> Execute(RunRequest request, CancellationToken ct)
    {
        // Yield first so the lock is released before any real work starts.
        await Task.Yield();

        try
        {
            var result = await runner.RunAsync(request, ct);
            LastResult = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            var cancelled = new RunResult(false, "The run was cancelled.", false, []);
            LastResult = cancelled;
            return cancelled;
        }
        catch (Exception e)
        {
            log.Error($"The run stopped unexpectedly: {e.Message}");
            var failed = new RunResult(true, $"The run stopped unexpectedly: {e.Message}", false, []);
            LastResult = failed;
            return failed;
        }
        finally
        {
            CurrentMapName = null;
        }
    }
}
