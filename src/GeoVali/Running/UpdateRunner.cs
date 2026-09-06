using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Vali;

namespace GeoVali.Running;

/// <summary>
/// One run, start to finish. Serial, one map at a time: vali generation is CPU- and network-heavy,
/// and serial execution keeps resource use predictable and progress legible.
/// </summary>
public sealed class UpdateRunner(
    IValiRunner vali,
    IGeoguessrClient geoguessr,
    RunLog log,
    Func<string?> mapsRootProvider,
    Func<int> defaultCadenceProvider,
    Func<DateTime> nowLocalProvider,
    Action<string?>? onMapChanged = null)
{
    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        var outcomes = new List<MapRunOutcome>();

        var mapsRoot = mapsRootProvider();
        if (string.IsNullOrWhiteSpace(mapsRoot) || !Directory.Exists(mapsRoot))
        {
            return Preflight("No maps folder is configured yet. Pick one in Settings.");
        }

        // Preflight 1: vali on PATH. Detect only; never install it for the user.
        if (vali.FindExecutable() is null)
        {
            return Preflight($"vali was not found on your PATH. Install it with:  {ValiRunner.InstallCommand}");
        }

        // Preflight 2: the cookie, BEFORE any generation. Generating a folder can take tens of
        // minutes, and a stale cookie is the most likely failure, so finding out afterwards must
        // be structurally impossible.
        string? nick;
        try
        {
            nick = await geoguessr.GetSignedInUserNickAsync(ct);
        }
        catch (GeoguessrAuthException)
        {
            nick = null;
        }

        if (nick is null)
        {
            log.Error("GeoGuessr rejected the stored cookie. Nothing was regenerated.");
            return new RunResult(true, new GeoguessrAuthException().Message, authInvalid: true, []);
        }

        log.Info($"Signed in to GeoGuessr as {nick}.");

        var candidates = MapScanner.Scan(mapsRoot)
            .Where(map => map.IsConfigured)
            .Where(map => request.Scope != RunScope.Single ||
                          string.Equals(map.Directory, request.SingleMapDirectory, StringComparison.Ordinal));

        try
        {
            foreach (var map in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var metadata = await MapMetadataStore.ReadMetadataAsync(map.Directory);
                if (metadata is null)
                {
                    continue;
                }

                var ephemeral = await MapMetadataStore.ReadEphemeralAsync(map.Directory);
                var name = string.IsNullOrWhiteSpace(metadata.name) ? map.FolderName : metadata.name;

                if (request.Scope == RunScope.DueOnly)
                {
                    var days = Cadence.DaysBetweenUpdates(metadata.updateFrequencyDays, defaultCadenceProvider());
                    if (!Cadence.IsDue(ephemeral.lastPublishedTimeUtc, days, nowLocalProvider()))
                    {
                        outcomes.Add(new MapRunOutcome(map.Directory, name, succeeded: false, skipped: true, error: null));
                        continue;
                    }
                }

                onMapChanged?.Invoke(name);

                try
                {
                    await RunOneMap(map.Directory, name, metadata, ephemeral, ct);
                    outcomes.Add(new MapRunOutcome(map.Directory, name, succeeded: true, skipped: false, error: null));
                }
                catch (GeoguessrAuthException e)
                {
                    // Not a per-map failure. Stop the run so it is not recorded as forty of them.
                    log.Error($"{e.Message} Stopping the run after {name}.");
                    return new RunResult(false, null, authInvalid: true, outcomes);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    log.Error($"{name}: {e.Message}");
                    await MapMetadataStore.WriteEphemeralAsync(map.Directory, ephemeral with
                    {
                        lastRunUtc = DateTime.UtcNow,
                        lastError = e.Message
                    });
                    outcomes.Add(new MapRunOutcome(map.Directory, name, succeeded: false, skipped: false, error: e.Message));
                }
            }
        }
        finally
        {
            onMapChanged?.Invoke(null);
        }

        return new RunResult(false, null, authInvalid: false, outcomes);

        RunResult Preflight(string message)
        {
            log.Error(message);
            return new RunResult(true, message, authInvalid: false, []);
        }
    }

    private async Task RunOneMap(
        string directory,
        string name,
        GeoguessrMetadata metadata,
        EphemeralMetadata ephemeral,
        CancellationToken ct)
    {
        log.Info($"Regenerating {name}");
        var generation = await vali.GenerateAsync(directory, line => log.Info($"{name}: {line}"), ct);
        if (generation.ExitCode != 0)
        {
            throw new NotPublishableException(
                $"vali failed with exit code {generation.ExitCode}. Last output: {generation.LastOutputLine}");
        }

        var locations = await LocationFile.ReadAsync(directory, ct);

        // Before any HTTP call, so GeoGuessr never answers a bare 400 we would have to decode.
        MapPublishGuard.EnsurePublishable(name, locations.Count);

        // Generated once and persisted, so the thumbnail is stable across runs.
        var avatar = metadata.avatar ?? AvatarGenerator.Generate();

        log.Info($"Publishing {name} with {locations.Count} locations.");
        var mapId = await geoguessr.PublishAsync(
            new PublishRequest(
                MapId: metadata.id,
                Name: name,
                Description: DescriptionTemplate.Expand(metadata.description, locations.Count),
                Avatar: avatar,
                Publish: metadata.published,
                Locations: locations),
            ct);

        // Write geoguessr.json back only when something in it actually changed: a freshly created
        // id, or an avatar we just generated. The description written back keeps the token, never
        // the substituted text.
        if (metadata.id != mapId || metadata.avatar is null)
        {
            await MapMetadataStore.WriteMetadataAsync(directory, metadata with { id = mapId, avatar = avatar });
        }

        await MapMetadataStore.WriteEphemeralAsync(directory, ephemeral with
        {
            lastPublishedTimeUtc = DateTime.UtcNow,
            updateCount = ephemeral.updateCount + 1,
            lastRunUtc = DateTime.UtcNow,
            lastError = null
        });

        log.Info($"Published {name}. https://www.geoguessr.com/maps/{mapId}");
    }
}
