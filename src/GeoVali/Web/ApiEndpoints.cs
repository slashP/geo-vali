using GeoVali.Autostart;
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Vali;

namespace GeoVali.Web;

public static class ApiEndpoints
{
    public static WebApplication MapGeoValiApi(this WebApplication app)
    {
        // Cheap identity probe. A starting instance uses it to tell "GeoVali already owns this
        // port" from "something else does", so /api/status stays free to be the expensive one.
        app.MapGet("/api/ping", () => Results.Json(new
        {
            product = AppInfo.ProductName,
            version = AppInfo.Version
        }));

        app.MapGet("/api/status", async (
            ConfigStore config,
            CredentialStore credentials,
            IValiRunner vali,
            IGeoguessrClient geoguessr,
            RunCoordinator coordinator,
            CancellationToken ct) =>
        {
            var current = config.Read();
            var hasCookie = !string.IsNullOrEmpty(credentials.ReadCookie());

            string? nick = null;
            if (hasCookie)
            {
                try
                {
                    nick = await geoguessr.GetSignedInUserNickAsync(ct);
                }
                catch (Exception e) when (e is GeoguessrAuthException or HttpRequestException)
                {
                    nick = null;
                }
            }

            return Results.Json(new StatusResponse(
                setupComplete: !string.IsNullOrWhiteSpace(current.mapsRoot) && hasCookie,
                mapsRoot: current.mapsRoot,
                valiInstalled: vali.FindExecutable() is not null,
                valiInstallCommand: ValiRunner.InstallCommand,
                authValid: nick is not null,
                userNick: nick,
                nextRunUtc: coordinator.NextScheduledRunUtc,
                running: coordinator.IsRunning,
                currentMap: coordinator.CurrentMapName,
                version: AppInfo.Version));
        });

        app.MapGet("/api/maps", async (ConfigStore config) =>
        {
            var current = config.Read();
            if (string.IsNullOrWhiteSpace(current.mapsRoot))
            {
                return Results.Json(Array.Empty<MapRow>());
            }

            var rows = new List<MapRow>();
            foreach (var map in MapScanner.Scan(current.mapsRoot))
            {
                var metadata = map.IsConfigured ? await MapMetadataStore.ReadMetadataAsync(map.Directory) : null;
                var ephemeral = await MapMetadataStore.ReadEphemeralAsync(map.Directory);
                var cadenceDays = Cadence.DaysBetweenUpdates(metadata?.updateFrequencyDays, current.defaultCadenceDays);

                rows.Add(new MapRow(
                    directory: map.Directory,
                    folderName: map.FolderName,
                    configured: metadata is not null,
                    name: string.IsNullOrWhiteSpace(metadata?.name) ? map.FolderName : metadata.name,
                    published: metadata?.published ?? false,
                    cadenceDays: cadenceDays,
                    lastPublishedTimeUtc: ephemeral.lastPublishedTimeUtc == DateTime.MinValue
                        ? null
                        : ephemeral.lastPublishedTimeUtc,
                    updateCount: ephemeral.updateCount,
                    lastError: ephemeral.lastError,
                    due: metadata is not null &&
                         Cadence.IsDue(ephemeral.lastPublishedTimeUtc, cadenceDays, DateTime.Now)));
            }

            return Results.Json(rows);
        });

        app.MapPost("/api/run", async (
            RunRequestBody body,
            ConfigStore config,
            RunCoordinator coordinator,
            CancellationToken ct) =>
        {
            var scope = body.scope?.ToLowerInvariant() switch
            {
                "all" => RunScope.All,
                "single" => RunScope.Single,
                _ => RunScope.DueOnly
            };

            if (scope == RunScope.Single)
            {
                if (string.IsNullOrWhiteSpace(body.directory))
                {
                    return Results.BadRequest(new { error = "Which map? No directory was supplied." });
                }

                var mapsRoot = config.Current.mapsRoot;
                if (string.IsNullOrWhiteSpace(mapsRoot) || !IsInside(mapsRoot, body.directory))
                {
                    return Results.BadRequest(new { error = "That folder is not inside the configured maps folder." });
                }
            }

            var result = await coordinator.RunAsync(new RunRequest(scope, body.directory), ct);
            return Results.Json(result);
        });

        app.MapGet("/api/events", async (HttpContext context, RunLog log, CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            // Nothing is proxied in front of a loopback server, but be explicit anyway.
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var queue = System.Threading.Channels.Channel.CreateBounded<LogEntry>(
                new System.Threading.Channels.BoundedChannelOptions(1000)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                });

            void Forward(LogEntry entry) => queue.Writer.TryWrite(entry);
            log.Appended += Forward;

            try
            {
                // Flush the headers before anything is buffered, so the client's stream opens
                // immediately rather than waiting for the first log line.
                await context.Response.Body.FlushAsync(ct);

                // Replay what already happened so a page opened mid-run is not blank.
                foreach (var entry in log.Recent())
                {
                    await Write(entry);
                }

                await foreach (var entry in queue.Reader.ReadAllAsync(ct))
                {
                    await Write(entry);
                }
            }
            catch (OperationCanceledException)
            {
                // The dashboard tab was closed.
            }
            finally
            {
                log.Appended -= Forward;
            }

            async Task Write(LogEntry entry)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(entry);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        });

        app.MapGet("/api/browse", (string? path) => Results.Json(FolderBrowser.List(path)));

        app.MapPost("/api/setup/folder", (FolderBody body, ConfigStore config) =>
        {
            if (string.IsNullOrWhiteSpace(body.path) || !Directory.Exists(body.path))
            {
                return Results.BadRequest(new { error = $"That folder does not exist: {body.path}" });
            }

            var full = Path.GetFullPath(body.path);
            config.Write(config.Current with { mapsRoot = full });
            return Results.Json(new { ok = true, mapCount = MapScanner.Scan(full).Count });
        });

        app.MapPost("/api/setup/cookie", async (
            CookieBody body,
            CredentialStore credentials,
            IGeoguessrClient geoguessr,
            RunLog log,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.cookie))
            {
                return Results.BadRequest(new { error = "Paste the _ncfa cookie value before saving." });
            }

            var candidate = body.cookie.Trim();

            // Validate before storing, so a wrong paste never becomes the saved state.
            // The store is written first because the HttpClient reads the cookie from it, then
            // rolled back if GeoGuessr rejects it.
            var previous = credentials.ReadCookie();
            credentials.WriteCookie(candidate);

            string? nick;
            try
            {
                nick = await geoguessr.GetSignedInUserNickAsync(ct);
            }
            catch (Exception e) when (e is GeoguessrAuthException or HttpRequestException)
            {
                nick = null;
            }

            if (nick is null)
            {
                if (previous is null)
                {
                    credentials.Clear();
                }
                else
                {
                    credentials.WriteCookie(previous);
                }

                log.Error("A pasted cookie was rejected by GeoGuessr.");
                return Results.BadRequest(new
                {
                    error = "GeoGuessr did not accept that value. In your browser press F12, open " +
                            "Application, then Cookies, then geoguessr.com, and copy the whole value of _ncfa."
                });
            }

            log.Info($"Signed in to GeoGuessr as {nick}.");
            return Results.Json(new { ok = true, nick });
        });

        app.MapPost("/api/maps/create", async (CreateMapBody body, ConfigStore config) =>
        {
            var validation = ValidateMapFolder(config, body.directory);
            if (validation is not null)
            {
                return validation;
            }

            if (string.IsNullOrWhiteSpace(body.name))
            {
                return Results.BadRequest(new { error = "Give the map a name." });
            }

            if (await MapMetadataStore.ReadMetadataAsync(body.directory!) is not null)
            {
                return Results.BadRequest(new
                {
                    error = "That folder already has a geoguessr.json. Edit it by hand to change the name or description."
                });
            }

            await MapMetadataStore.WriteMetadataAsync(body.directory!, new GeoguessrMetadata
            {
                id = null,                          // filled in on the first publish
                name = body.name!.Trim(),
                description = (body.description ?? "").Trim(),
                avatar = AvatarGenerator.Generate(), // generated once, then stable across runs
                published = true,
                updateFrequencyDays = null
            });

            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/maps/link", async (
            LinkMapBody body,
            ConfigStore config,
            IGeoguessrClient geoguessr,
            CancellationToken ct) =>
        {
            var validation = ValidateMapFolder(config, body.directory);
            if (validation is not null)
            {
                return validation;
            }

            if (!MapUrlParser.TryExtractMapId(body.url ?? "", out var mapId))
            {
                return Results.BadRequest(new
                {
                    error = "That does not look like a GeoGuessr map link. " +
                            "Paste the address of the map page, for example https://www.geoguessr.com/maps/60a19170089ecc0001db6609"
                });
            }

            // Reuses a call already in the publish path: success proves the signed-in user owns it.
            DraftInfo? draft;
            try
            {
                draft = await geoguessr.GetDraftAsync(mapId, ct);
            }
            catch (GeoguessrAuthException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }

            if (draft is null)
            {
                return Results.BadRequest(new
                {
                    error = $"Map {mapId} could not be found on your account. " +
                            "Make sure you are signed in as the user who owns it."
                });
            }

            await MapMetadataStore.WriteMetadataAsync(body.directory!, new GeoguessrMetadata
            {
                id = draft.id,
                name = draft.name,
                description = "",
                avatar = AvatarGenerator.Generate(),
                published = true,
                updateFrequencyDays = null
            });

            return Results.Json(new { ok = true, name = draft.name, id = draft.id });
        });

        app.MapGet("/api/settings", (ConfigStore config, IAutostart autostart) =>
        {
            var current = config.Read();
            return Results.Json(new
            {
                mapsRoot = current.mapsRoot,
                defaultCadenceDays = current.defaultCadenceDays,
                checkIntervalMinutes = current.checkIntervalMinutes,
                dashboardPort = current.dashboardPort,
                startAtLogin = autostart.IsEnabled(),
                autostartDescription = autostart.Describe(),
                credentialProtectionNote = OperatingSystem.IsWindows()
                    ? "Your GeoGuessr cookie is encrypted with Windows DPAPI for your account."
                    : "Your GeoGuessr cookie is stored in a file only your user can read (mode 0600). " +
                      "It is not encrypted. You can revoke it any time by signing out of GeoGuessr."
            });
        });

        app.MapPost("/api/settings", (SettingsBody body, ConfigStore config, IAutostart autostart) =>
        {
            var current = config.Read();

            if (body.defaultCadenceDays is not null && body.defaultCadenceDays < 1)
            {
                return Results.BadRequest(new { error = "The default cadence must be at least 1 day." });
            }

            if (body.checkIntervalMinutes is not null && body.checkIntervalMinutes < 1)
            {
                return Results.BadRequest(new { error = "The check interval must be at least 1 minute." });
            }

            if (body.dashboardPort is not null && body.dashboardPort is < 1024 or > 65535)
            {
                return Results.BadRequest(new { error = "The dashboard port must be between 1024 and 65535." });
            }

            if (body.mapsRoot is not null && !Directory.Exists(body.mapsRoot))
            {
                return Results.BadRequest(new { error = $"That folder does not exist: {body.mapsRoot}" });
            }

            // Registering start at login can fail for reasons only the user can resolve — on
            // Windows it needs one administrator approval — so do it before anything is written.
            // A switch saved as on while nothing was registered is the one outcome worth avoiding.
            if (body.startAtLogin is not null)
            {
                try
                {
                    if (body.startAtLogin.Value)
                    {
                        autostart.Enable();
                    }
                    else
                    {
                        autostart.Disable();
                    }
                }
                catch (AutostartException e)
                {
                    return Results.BadRequest(new { error = e.Message });
                }
            }

            var updated = current with
            {
                mapsRoot = body.mapsRoot is null ? current.mapsRoot : Path.GetFullPath(body.mapsRoot),
                defaultCadenceDays = body.defaultCadenceDays ?? current.defaultCadenceDays,
                checkIntervalMinutes = body.checkIntervalMinutes ?? current.checkIntervalMinutes,
                dashboardPort = body.dashboardPort ?? current.dashboardPort,
                startAtLogin = body.startAtLogin ?? current.startAtLogin
            };

            config.Write(updated);

            return Results.Json(new
            {
                ok = true,
                restartRequired = body.dashboardPort is not null && body.dashboardPort != current.dashboardPort
            });
        });

        app.MapPost("/api/settings/cookie/clear", (CredentialStore credentials) =>
        {
            credentials.Clear();
            return Results.Json(new { ok = true });
        });

        return app;
    }

    /// <summary>Returns a 400 result when the folder is unusable, or null when it is fine.</summary>
    private static IResult? ValidateMapFolder(ConfigStore config, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Results.BadRequest(new { error = "That folder does not exist." });
        }

        var mapsRoot = config.Current.mapsRoot;
        if (string.IsNullOrWhiteSpace(mapsRoot) || !IsInside(mapsRoot, directory))
        {
            return Results.BadRequest(new { error = "That folder is not inside the configured maps folder." });
        }

        if (!File.Exists(MapPaths.Definition(directory)))
        {
            return Results.BadRequest(new { error = $"That folder has no {MapPaths.DefinitionFileName}." });
        }

        return null;
    }

    /// <summary>Guards against a request naming a folder outside the user's maps tree.</summary>
    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return normalizedCandidate.Equals(normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
