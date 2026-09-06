# GeoVali Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `geovali`, a .NET global tool that watches a folder of vali map definitions and, on a per-map cadence, regenerates each map with the `vali` CLI and republishes it to GeoGuessr, driven from a local web dashboard.

**Architecture:** One ASP.NET Core process. A `BackgroundService` timer fires an `UpdateRunner` that works serially through map folders: preflight (vali present, cookie valid), then per map `vali generate` → read locations → four-call GeoGuessr publish → stamp state back to JSON beside the map. The two things that touch the outside world (`IValiRunner`, `IGeoguessrClient`) sit behind interfaces so nothing in the test suite shells out or opens a socket to geoguessr.com. The UI is embedded static HTML with Server-Sent Events for live progress.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, System.Text.Json, xunit, vanilla JavaScript (no build step).

**Spec:** `docs/superpowers/specs/2026-09-06-vali-geoguessr-automation-design.md`

## Global Constraints

Every task's requirements implicitly include this section.

- **Target framework `net10.0`.** Single project, packed as a .NET global tool: `PackageId` = `GeoVali`, `ToolCommandName` = `geovali`.
- **No frontend build step.** No npm, no bundler. Vanilla JS/CSS/HTML embedded as assembly resources.
- **No database.** All durable state is plain JSON beside the map folder.
- **The `_ncfa` cookie is never logged, never included in an error message, and never sent anywhere except `https://www.geoguessr.com`.**
- **Never modify the user's `.gitignore`.** Document the suggested entry in the README instead.
- **The dashboard binds to loopback only** (`http://127.0.0.1:<port>`). Default port `5099`, next free port if taken.
- **Defaults:** global cadence `7` days, scheduler check interval `30` minutes.
- **`MinimumLocationCount = 5`.** Fewer than 5 locations fails the map before any HTTP call.
- **GeoGuessr base address `https://www.geoguessr.com/`**, every request carrying `Cookie: _ncfa=<value>`.
- **No test touches the real network or the real `vali`.**
- **Errors are user-facing sentences** that name what went wrong and what to do about it. Stack traces go to the log file, never to the dashboard as the primary surface.
- **Out of scope, do not build:** editing map name/description/tags from the UI after creation, installing or updating vali, browsing a picker of the user's existing GeoGuessr maps.
- **On-disk JSON property names are lowercase-first and match the file exactly** (`id`, `name`, `updateFrequencyDays`, `lastPublishedTimeUtc`). Model properties use those exact names so no serializer naming policy is needed and existing files from the author's tree round-trip unchanged.
- **Method:** TDD — failing test first, minimal implementation, green, commit. Commit at the end of every task.

## File Structure

```
GeoVali.sln
src/GeoVali/
  GeoVali.csproj                    Web SDK, PackAsTool, embedded wwwroot
  Program.cs                         Host wiring, port selection, browser launch
  AppInfo.cs                         Product name + version, shown in the dashboard header
  Configuration/
    AppPaths.cs                      Per-OS config directory
    AppConfig.cs                     Config record + defaults
    ConfigStore.cs                   Read/write config.json
    CredentialStore.cs               Read/write the protected _ncfa cookie
    ICredentialProtector.cs          DPAPI on Windows, 0600 file on Unix
  Maps/
    MapPaths.cs                      The four filenames, resolved against a map directory
    MapModels.cs                     GeoguessrMetadata, MapAvatar, EphemeralMetadata, ValiLocation
    MapMetadataStore.cs              Read/write geoguessr.json and geoguessr.ephemeral.json
    MapScanner.cs                    Recursive discovery + configured/not-configured classification
    Cadence.cs                       Pure due/not-due arithmetic
    LocationFile.cs                  Stream-read map-locations.json
    MapPublishGuard.cs               Minimum-location guard
    DescriptionTemplate.cs           {{LocationCount}} expansion
  Vali/
    IValiRunner.cs                   Interface: availability + generate
    ValiRunner.cs                    Process invocation, stdout streaming, ANSI stripping
  Geoguessr/
    IGeoguessrClient.cs              Interface + DTOs + exceptions
    GeoguessrClient.cs               Auth probe, draft create/read/update, publish
    TransientRetryHandler.cs         Bounded retry on transient failures
    AvatarGenerator.cs               Random avatar from GeoGuessr's option lists
    MapUrlParser.cs                  Extract a map id from a pasted GeoGuessr URL
  Running/
    RunLog.cs                        Rolling file + in-memory ring buffer + redaction
    RunModels.cs                     RunRequest, RunScope, MapRunOutcome, RunResult
    UpdateRunner.cs                  Preflight + per-map orchestration
    RunCoordinator.cs                Single run-lock; manual runs join an in-flight run
    Scheduler.cs                     BackgroundService timer
  Autostart/
    IAutostart.cs                    Interface
    Autostart.cs                     Windows/macOS/Linux implementations + factory
  Web/
    ApiEndpoints.cs                  All /api endpoints
    FolderBrowser.cs                 Directory listing for the folder picker
    LocalOnlyMiddleware.cs           Loopback + custom-header guard
  wwwroot/
    index.html  app.js  style.css
tests/GeoVali.Tests/
  GeoVali.Tests.csproj
  CadenceTests.cs  MapScannerTests.cs  MapMetadataStoreTests.cs
  LocationFileTests.cs  MapPublishGuardTests.cs  DescriptionTemplateTests.cs
  GeoguessrClientTests.cs  TransientRetryHandlerTests.cs  MapUrlParserTests.cs
  ValiRunnerTests.cs  ConfigStoreTests.cs  CredentialStoreTests.cs
  RunLogTests.cs  UpdateRunnerTests.cs  RunCoordinatorTests.cs
  EndToEndRunTests.cs  ApiEndpointTests.cs
  Support/RecordingHandler.cs  Support/FakeValiRunner.cs
  Support/FakeGeoguessrClient.cs  Support/TempDir.cs  Support/StubGeoguessrServer.cs
  Support/FakeValiExecutable.cs
```

---

### Task 1: Solution skeleton packaged as a global tool

**Files:**
- Create: `GeoVali.sln`
- Create: `src/GeoVali/GeoVali.csproj`
- Create: `src/GeoVali/Program.cs`
- Create: `src/GeoVali/AppInfo.cs`
- Create: `tests/GeoVali.Tests/GeoVali.Tests.csproj`
- Test: `tests/GeoVali.Tests/AppInfoTests.cs`
- Create: `.gitattributes`

**Interfaces:**
- Consumes: nothing.
- Produces: `GeoVali.AppInfo.ProductName` (string, `"GeoVali"`), `GeoVali.AppInfo.Version` (string). A buildable app project and a test project that references it. `public partial class Program` so `WebApplicationFactory<Program>` works in Task 14.

- [x] **Step 1: Create the solution and both projects**

```bash
cd /home/perhel/geo-vali
dotnet new sln -n GeoVali
dotnet new web -o src/GeoVali -f net10.0
dotnet new xunit -o tests/GeoVali.Tests -f net10.0
dotnet sln add src/GeoVali/GeoVali.csproj tests/GeoVali.Tests/GeoVali.Tests.csproj
dotnet add tests/GeoVali.Tests/GeoVali.Tests.csproj reference src/GeoVali/GeoVali.csproj
```

The `xunit` template may generate xunit v2 or v3 depending on SDK band. Either is fine — every test in this plan uses only `[Fact]`, `[Theory]`, `[InlineData]` and `Assert`, which are identical in both.

- [x] **Step 2: Replace the app csproj**

`src/GeoVali/GeoVali.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>GeoVali</RootNamespace>
    <AssemblyName>GeoVali</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <PackAsTool>true</PackAsTool>
    <ToolCommandName>geovali</ToolCommandName>
    <PackageId>GeoVali</PackageId>
    <Version>0.1.0</Version>
    <Description>Regenerates vali GeoGuessr maps on a schedule and republishes them.</Description>
    <PackageOutputPath>$(MSBuildThisFileDirectory)../../nupkg</PackageOutputPath>
  </PropertyGroup>

</Project>
```

- [x] **Step 3: Write `AppInfo`**

`src/GeoVali/AppInfo.cs`:

```csharp
using System.Reflection;

namespace GeoVali;

/// <summary>Identity of the running tool, shown in the dashboard header and the log file.</summary>
public static class AppInfo
{
    public const string ProductName = "GeoVali";

    public static string Version { get; } =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";
}
```

- [x] **Step 4: Write the failing test**

`tests/GeoVali.Tests/AppInfoTests.cs`:

```csharp
using GeoVali;
using Xunit;

namespace GeoVali.Tests;

public class AppInfoTests
{
    [Fact]
    public void Reports_product_name_and_a_non_empty_version()
    {
        Assert.Equal("GeoVali", AppInfo.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.NotEqual("0.0.0", AppInfo.Version);
    }
}
```

- [x] **Step 5: Run the test and watch it fail**

Run: `dotnet test tests/GeoVali.Tests/GeoVali.Tests.csproj`
Expected: FAIL before Step 3's file exists; after Step 3 it should pass. If you did Step 3 first, delete `AppInfo.cs`, confirm the compile error `The name 'AppInfo' does not exist`, then restore it. The point is to see red before green.

- [x] **Step 6: Make `Program` testable and minimal**

`src/GeoVali/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/api/version", () => new { product = GeoVali.AppInfo.ProductName, version = GeoVali.AppInfo.Version });
app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
```

- [x] **Step 7: Verify build, test and pack**

Run:
```bash
dotnet build
dotnet test
dotnet pack src/GeoVali/GeoVali.csproj -c Release
ls nupkg/
```
Expected: build succeeds, the `AppInfo` test passes, and `nupkg/GeoVali.0.1.0.nupkg` exists.

- [x] **Step 8: Add `.gitattributes` so the fake-vali script in Task 13 keeps LF endings**

`.gitattributes`:

```
* text=auto
*.sh text eol=lf
*.cmd text eol=crlf
```

- [x] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: scaffold GeoVali solution as a .NET global tool"
```

---

### Task 2: Cadence arithmetic

**Files:**
- Create: `src/GeoVali/Maps/Cadence.cs`
- Test: `tests/GeoVali.Tests/CadenceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `GeoVali.Maps.Cadence.DefaultDaysBetweenUpdates` → `const int` = `7`
  - `GeoVali.Maps.Cadence.DaysBetweenUpdates(int? perMapOverrideDays, int globalDefaultDays)` → `int`
  - `GeoVali.Maps.Cadence.IsDue(DateTime lastPublishedTimeUtc, int daysBetweenUpdates, DateTime nowLocal)` → `bool`

`nowLocal` is a parameter rather than a call to `DateTime.Now` so the calendar-day boundary is testable. Callers pass `DateTime.Now`.

- [x] **Step 1: Write the failing tests**

`tests/GeoVali.Tests/CadenceTests.cs`:

```csharp
using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class CadenceTests
{
    // Helper: build a UTC instant that lands on the given local date at local noon,
    // so the test is independent of the machine's time zone.
    private static DateTime UtcForLocalNoonOn(int year, int month, int day) =>
        new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    [Theory]
    [InlineData(null, 7, 7)]      // no override -> global default
    [InlineData(0, 7, 7)]         // zero is not a valid override -> global default
    [InlineData(-3, 7, 7)]        // negative is not a valid override -> global default
    [InlineData(10, 7, 10)]       // per-map override wins
    [InlineData(1, 30, 1)]        // per-map override wins even when smaller
    public void Resolves_per_map_override_before_global_default(int? overrideDays, int globalDefault, int expected)
    {
        Assert.Equal(expected, Cadence.DaysBetweenUpdates(overrideDays, globalDefault));
    }

    [Fact]
    public void Global_default_is_seven_days()
    {
        Assert.Equal(7, Cadence.DefaultDaysBetweenUpdates);
    }

    [Fact]
    public void Every_one_day_means_not_already_done_today()
    {
        var today = new DateTime(2026, 9, 6, 9, 0, 0, DateTimeKind.Local);

        // Published earlier the same local day -> not due.
        Assert.False(Cadence.IsDue(UtcForLocalNoonOn(2026, 9, 6), 1, today));

        // Published yesterday -> due, no matter what time of day.
        Assert.True(Cadence.IsDue(UtcForLocalNoonOn(2026, 9, 5), 1, today));
    }

    [Fact]
    public void Counts_whole_local_calendar_days_not_elapsed_hours()
    {
        var today = new DateTime(2026, 9, 6, 0, 30, 0, DateTimeKind.Local);

        // 10 local days earlier: exactly due.
        Assert.True(Cadence.IsDue(UtcForLocalNoonOn(2026, 8, 27), 10, today));

        // 9 local days earlier: one day short, even though far more than
        // 9 * 24 hours may have elapsed depending on the clock time.
        Assert.False(Cadence.IsDue(UtcForLocalNoonOn(2026, 8, 28), 10, today));
    }

    [Fact]
    public void A_map_that_has_never_been_published_is_always_due()
    {
        var today = new DateTime(2026, 9, 6, 9, 0, 0, DateTimeKind.Local);
        Assert.True(Cadence.IsDue(DateTime.MinValue, 20, today));
    }

    [Fact]
    public void Treats_an_unspecified_kind_timestamp_as_utc()
    {
        // geoguessr.ephemeral.json written by an older tool may lack the trailing Z.
        var today = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Unspecified);

        // Interpreted as UTC, that is earlier today in any time zone within +/- 10h,
        // so a 1-day cadence is not yet due.
        var result = Cadence.IsDue(unspecified, 1, today);
        var expected = DateOnly.FromDateTime(today) ==
                       DateOnly.FromDateTime(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).ToLocalTime());
        Assert.Equal(!expected, result);
    }
}
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CadenceTests`
Expected: FAIL — `The type or namespace name 'Cadence' could not be found`.

- [x] **Step 3: Write the implementation**

`src/GeoVali/Maps/Cadence.cs`:

```csharp
namespace GeoVali.Maps;

/// <summary>
/// Single source of truth for how often a map regenerates and re-uploads.
/// Cadence is measured in whole <em>local calendar days</em> and gated on the local date, so
/// "every 1 day" means "regenerate if it wasn't already done today" regardless of how long the
/// previous run took. Ported from the author's MapCadence, minus the folder-name special cases.
/// </summary>
public static class Cadence
{
    /// <summary>Used when a map does not set <c>updateFrequencyDays</c> and the user has not changed the setting.</summary>
    public const int DefaultDaysBetweenUpdates = 7;

    /// <summary>Resolution order: per-map override, then the global default.</summary>
    public static int DaysBetweenUpdates(int? perMapOverrideDays, int globalDefaultDays) =>
        perMapOverrideDays is > 0 ? perMapOverrideDays.Value : globalDefaultDays;

    /// <summary>
    /// True when at least <paramref name="daysBetweenUpdates"/> local calendar days have elapsed
    /// since <paramref name="lastPublishedTimeUtc"/>.
    /// </summary>
    public static bool IsDue(DateTime lastPublishedTimeUtc, int daysBetweenUpdates, DateTime nowLocal)
    {
        if (lastPublishedTimeUtc == DateTime.MinValue)
        {
            return true;
        }

        var utc = lastPublishedTimeUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(lastPublishedTimeUtc, DateTimeKind.Utc)
            : lastPublishedTimeUtc;

        var lastLocalDate = DateOnly.FromDateTime(utc.ToLocalTime());
        var today = DateOnly.FromDateTime(nowLocal);
        return today.DayNumber - lastLocalDate.DayNumber >= daysBetweenUpdates;
    }
}
```

- [x] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CadenceTests`
Expected: PASS, all 9 test cases.

- [x] **Step 5: Commit**

```bash
git add src/GeoVali/Maps/Cadence.cs tests/GeoVali.Tests/CadenceTests.cs
git commit -m "feat: cadence arithmetic in whole local calendar days"
```

---

### Task 3: Map discovery

**Files:**
- Create: `src/GeoVali/Maps/MapPaths.cs`
- Create: `src/GeoVali/Maps/MapScanner.cs`
- Test: `tests/GeoVali.Tests/MapScannerTests.cs`
- Test: `tests/GeoVali.Tests/Support/TempDir.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `GeoVali.Maps.MapPaths` — `const string DefinitionFileName = "map.json"`, `MetadataFileName = "geoguessr.json"`, `EphemeralFileName = "geoguessr.ephemeral.json"`, `LocationsFileName = "map-locations.json"`, and `Definition(string directory)`, `Metadata(string directory)`, `Ephemeral(string directory)`, `Locations(string directory)` all returning `string`.
  - `GeoVali.Maps.MapFolder` — `sealed record MapFolder(string Directory, string FolderName, bool IsConfigured)`.
  - `GeoVali.Maps.MapScanner.Scan(string root)` → `IReadOnlyList<MapFolder>`, ordered by `Directory` ordinal.
  - Test helper `GeoVali.Tests.Support.TempDir` — `IDisposable`, property `Path`, methods `Dir(params string[] segments)` → `string` and `File(string relativePath, string contents)` → `string`.

- [x] **Step 1: Write the temp-directory test helper**

`tests/GeoVali.Tests/Support/TempDir.cs`:

```csharp
namespace GeoVali.Tests.Support;

/// <summary>A throwaway directory tree that deletes itself at the end of a test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "geovali-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Creates a nested directory and returns its full path.</summary>
    public string Dir(params string[] segments)
    {
        var full = System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Writes a file (creating parent directories) and returns its full path.</summary>
    public string File(string relativePath, string contents)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test left a handle open; the OS temp cleaner will get it.
        }
    }
}
```

- [x] **Step 2: Write the failing tests**

`tests/GeoVali.Tests/MapScannerTests.cs`:

```csharp
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
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MapScannerTests`
Expected: FAIL — `The type or namespace name 'MapScanner' could not be found`.

- [x] **Step 4: Write `MapPaths`**

`src/GeoVali/Maps/MapPaths.cs`:

```csharp
namespace GeoVali.Maps;

/// <summary>
/// A map is a folder. These are the four files that can live in it.
/// <c>vali generate --file &lt;dir&gt;/map.json</c> writes <c>map-locations.json</c> alongside the
/// definition, which is why the folder-as-map convention needs no configuration.
/// </summary>
public static class MapPaths
{
    /// <summary>Written by the user. The vali map definition. Source-controlled.</summary>
    public const string DefinitionFileName = "map.json";

    /// <summary>Written by GeoVali at setup, hand-edited afterwards. Source-controlled.</summary>
    public const string MetadataFileName = "geoguessr.json";

    /// <summary>Written by GeoVali every run. Regenerable, so not source-controlled.</summary>
    public const string EphemeralFileName = "geoguessr.ephemeral.json";

    /// <summary>Written by <c>vali generate</c>. Regenerable, so not source-controlled.</summary>
    public const string LocationsFileName = "map-locations.json";

    public static string Definition(string directory) => Path.Combine(directory, DefinitionFileName);
    public static string Metadata(string directory) => Path.Combine(directory, MetadataFileName);
    public static string Ephemeral(string directory) => Path.Combine(directory, EphemeralFileName);
    public static string Locations(string directory) => Path.Combine(directory, LocationsFileName);
}
```

- [x] **Step 5: Write `MapScanner`**

`src/GeoVali/Maps/MapScanner.cs`:

```csharp
namespace GeoVali.Maps;

/// <summary>A discovered map folder. <paramref name="IsConfigured"/> is false until it has a geoguessr.json.</summary>
public sealed record MapFolder(string Directory, string FolderName, bool IsConfigured);

/// <summary>Recursively finds map folders under a root.</summary>
public static class MapScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        // A permission-denied folder somewhere under the root must not abort the whole scan.
        IgnoreInaccessible = true,
        // Symlink loops in a maps tree would otherwise hang the scan.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        MatchType = MatchType.Simple
    };

    public static IReadOnlyList<MapFolder> Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, MapPaths.DefinitionFileName, Options)
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .Select(directory => new MapFolder(
                Directory: directory,
                FolderName: new DirectoryInfo(directory).Name,
                IsConfigured: File.Exists(MapPaths.Metadata(directory))))
            .ToList();
    }
}
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MapScannerTests`
Expected: PASS, 6 tests.

- [x] **Step 7: Commit**

```bash
git add src/GeoVali/Maps/MapPaths.cs src/GeoVali/Maps/MapScanner.cs \
        tests/GeoVali.Tests/MapScannerTests.cs tests/GeoVali.Tests/Support/TempDir.cs
git commit -m "feat: discover map folders under the configured root"
```

---

### Task 4: Map metadata files and the description token

**Files:**
- Create: `src/GeoVali/Maps/MapModels.cs`
- Create: `src/GeoVali/Maps/MapMetadataStore.cs`
- Create: `src/GeoVali/Maps/DescriptionTemplate.cs`
- Test: `tests/GeoVali.Tests/MapMetadataStoreTests.cs`
- Test: `tests/GeoVali.Tests/DescriptionTemplateTests.cs`

**Interfaces:**
- Consumes: `MapPaths` (Task 3), `TempDir` (Task 3).
- Produces:
  - `GeoVali.Maps.MapAvatar` — `sealed record` with `string background, landscape, ground, decoration` (init, default `""`).
  - `GeoVali.Maps.GeoguessrMetadata` — `sealed record` with `string? id`, `string name`, `string description`, `MapAvatar? avatar`, `bool published`, `int? updateFrequencyDays`.
  - `GeoVali.Maps.EphemeralMetadata` — `sealed record` with `DateTime lastPublishedTimeUtc`, `int updateCount`, `DateTime? lastRunUtc`, `string? lastError`.
  - `GeoVali.Maps.ValiLocation` — `sealed record` with `double lat, lng, heading, pitch`, `string? panoId`.
  - `GeoVali.Maps.MapMetadataStore` — static, `Task<GeoguessrMetadata?> ReadMetadataAsync(string directory)`, `Task WriteMetadataAsync(string directory, GeoguessrMetadata metadata)`, `Task<EphemeralMetadata> ReadEphemeralAsync(string directory)`, `Task WriteEphemeralAsync(string directory, EphemeralMetadata ephemeral)`.
  - `GeoVali.Maps.DescriptionTemplate.Expand(string description, int locationCount)` → `string`.

Property names are deliberately lowercase-first to match the on-disk files byte for byte. `avatar` is nullable so "generate one if absent" is expressible.

- [x] **Step 1: Write the failing tests for the token**

`tests/GeoVali.Tests/DescriptionTemplateTests.cs`:

```csharp
using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class DescriptionTemplateTests
{
    [Fact]
    public void Replaces_the_location_count_token_with_the_number()
    {
        Assert.Equal(
            "1234 hand-picked coastal locations.",
            DescriptionTemplate.Expand("{{LocationCount}} hand-picked coastal locations.", 1234));
    }

    [Fact]
    public void Replaces_every_occurrence()
    {
        Assert.Equal(
            "7 in, 7 out",
            DescriptionTemplate.Expand("{{LocationCount}} in, {{LocationCount}} out", 7));
    }

    [Fact]
    public void Formats_the_count_invariantly_with_no_thousands_separator()
    {
        Assert.Equal("55000 locations", DescriptionTemplate.Expand("{{LocationCount}} locations", 55000));
    }

    [Fact]
    public void Leaves_a_description_without_the_token_untouched()
    {
        Assert.Equal("Just a map.", DescriptionTemplate.Expand("Just a map.", 42));
    }

    [Fact]
    public void Handles_a_null_or_empty_description()
    {
        Assert.Equal(string.Empty, DescriptionTemplate.Expand(null!, 42));
        Assert.Equal(string.Empty, DescriptionTemplate.Expand("", 42));
    }
}
```

- [x] **Step 2: Write the failing tests for the metadata store**

`tests/GeoVali.Tests/MapMetadataStoreTests.cs`:

```csharp
using System.Globalization;
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class MapMetadataStoreTests
{
    private const string RealWorldMetadata = """
        {
          "id": "60a19170089ecc0001db6609",
          "name": "An Arbitrary Africa",
          "description": "Explore Africa with {{LocationCount}} locations.",
          "avatar": {
            "background": "sunrise",
            "landscape": "desserthills",
            "ground": "darkbrown",
            "decoration": "smalltrees"
          },
          "published": true,
          "updateFrequencyDays": null
        }
        """;

    [Fact]
    public async Task Reads_a_geoguessr_json_written_by_hand()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Metadata(dir), RealWorldMetadata);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.NotNull(metadata);
        Assert.Equal("60a19170089ecc0001db6609", metadata!.id);
        Assert.Equal("An Arbitrary Africa", metadata.name);
        Assert.Equal("Explore Africa with {{LocationCount}} locations.", metadata.description);
        Assert.True(metadata.published);
        Assert.Null(metadata.updateFrequencyDays);
        Assert.Equal("sunrise", metadata.avatar!.background);
        Assert.Equal("desserthills", metadata.avatar.landscape);
    }

    [Fact]
    public async Task Tolerates_extra_fields_from_the_authors_older_pipeline()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("legacy");
        await File.WriteAllTextAsync(MapPaths.Metadata(dir), """
            { "mapDistributionLink": null, "id": "abc", "highlighted": true,
              "name": "Legacy", "description": "d", "published": true,
              "shouldIncludeExtremities": false, "updateFrequencyDays": 10 }
            """);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.Equal("abc", metadata!.id);
        Assert.Equal(10, metadata.updateFrequencyDays);
    }

    [Fact]
    public async Task Returns_null_when_the_map_is_not_set_up_yet()
    {
        using var temp = new TempDir();
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(temp.Dir("brand-new")));
    }

    [Fact]
    public async Task Round_trips_metadata_through_disk()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("round-trip");
        var original = new GeoguessrMetadata
        {
            id = "6a9d6c505a43d0a64f98be5c",
            name = "Coastal Sri Lanka",
            description = "{{LocationCount}} hand-picked coastal locations.",
            avatar = new MapAvatar { background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese" },
            published = true,
            updateFrequencyDays = 10
        };

        await MapMetadataStore.WriteMetadataAsync(dir, original);
        var reloaded = await MapMetadataStore.ReadMetadataAsync(dir);

        Assert.Equal(original, reloaded);
    }

    [Fact]
    public async Task Writes_metadata_indented_and_keeps_the_token_unsubstituted()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("pretty");

        await MapMetadataStore.WriteMetadataAsync(dir, new GeoguessrMetadata
        {
            name = "N", description = "{{LocationCount}} places.", published = true
        });

        var text = await File.ReadAllTextAsync(MapPaths.Metadata(dir));
        Assert.Contains("\n  \"name\": \"N\"", text.ReplaceLineEndings("\n"));
        Assert.Contains("{{LocationCount}}", text);
    }

    [Fact]
    public async Task Reads_ephemeral_state()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("stamped");
        await File.WriteAllTextAsync(MapPaths.Ephemeral(dir), """
            { "lastPublishedTimeUtc": "2026-09-05T23:18:45.6102689Z", "updateCount": 307 }
            """);

        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);

        Assert.Equal(
            DateTime.Parse(
                "2026-09-05T23:18:45.6102689Z",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.RoundtripKind),
            ephemeral.lastPublishedTimeUtc);
        Assert.Equal(307, ephemeral.updateCount);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task Defaults_ephemeral_state_when_the_file_is_missing_or_corrupt()
    {
        using var temp = new TempDir();

        var missing = await MapMetadataStore.ReadEphemeralAsync(temp.Dir("never-run"));
        Assert.Equal(DateTime.MinValue, missing.lastPublishedTimeUtc);
        Assert.Equal(0, missing.updateCount);

        var corruptDir = temp.Dir("corrupt");
        await File.WriteAllTextAsync(MapPaths.Ephemeral(corruptDir), "{ this is not json");
        var corrupt = await MapMetadataStore.ReadEphemeralAsync(corruptDir);
        Assert.Equal(DateTime.MinValue, corrupt.lastPublishedTimeUtc);
        Assert.Equal(0, corrupt.updateCount);
    }

    [Fact]
    public async Task Round_trips_ephemeral_state_including_the_last_error()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("errored");
        var ephemeral = new EphemeralMetadata
        {
            lastPublishedTimeUtc = new DateTime(2026, 9, 4, 22, 11, 4, DateTimeKind.Utc),
            updateCount = 37,
            lastRunUtc = new DateTime(2026, 9, 6, 3, 0, 12, DateTimeKind.Utc),
            lastError = "vali exited with code 1."
        };

        await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
        var reloaded = await MapMetadataStore.ReadEphemeralAsync(dir);

        Assert.Equal(ephemeral.lastPublishedTimeUtc, reloaded.lastPublishedTimeUtc);
        Assert.Equal(37, reloaded.updateCount);
        Assert.Equal("vali exited with code 1.", reloaded.lastError);
        Assert.Equal(ephemeral.lastRunUtc, reloaded.lastRunUtc);
    }
}
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MapMetadataStoreTests|FullyQualifiedName~DescriptionTemplateTests"`
Expected: FAIL — `MapMetadataStore` / `DescriptionTemplate` do not exist.

- [x] **Step 4: Write the models**

`src/GeoVali/Maps/MapModels.cs`:

```csharp
namespace GeoVali.Maps;

/// <summary>The four-part GeoGuessr map thumbnail. Generated once at setup, then left alone.</summary>
public sealed record MapAvatar
{
    public string background { get; init; } = "";
    public string landscape { get; init; } = "";
    public string ground { get; init; } = "";
    public string decoration { get; init; } = "";
}

/// <summary>
/// <c>geoguessr.json</c> — written by GeoVali when the map is set up, hand-edited afterwards,
/// and source-controlled by the user. Property names match the file exactly.
/// </summary>
public sealed record GeoguessrMetadata
{
    /// <summary>Absent until the map is first published, or filled immediately when linking an existing map.</summary>
    public string? id { get; init; }

    public string name { get; init; } = "";

    /// <summary>May contain the <c>{{LocationCount}}</c> token, which is expanded at publish time only.</summary>
    public string description { get; init; } = "";

    /// <summary>Null until GeoVali generates one; persisted afterwards so it is stable across runs.</summary>
    public MapAvatar? avatar { get; init; }

    /// <summary>When false, GeoVali updates the draft but never calls the publish endpoint.</summary>
    public bool published { get; init; } = true;

    /// <summary>Null means "use the global default".</summary>
    public int? updateFrequencyDays { get; init; }
}

/// <summary>
/// <c>geoguessr.ephemeral.json</c> — regenerable state, kept separate from the committed metadata
/// so the user can gitignore it.
/// </summary>
public sealed record EphemeralMetadata
{
    public DateTime lastPublishedTimeUtc { get; init; } = DateTime.MinValue;
    public int updateCount { get; init; }
    public DateTime? lastRunUtc { get; init; }
    public string? lastError { get; init; }
}

/// <summary>One entry of <c>map-locations.json</c>, as written by <c>vali generate</c>.</summary>
public sealed record ValiLocation
{
    public double lat { get; init; }
    public double lng { get; init; }
    public double heading { get; init; }

    /// <summary>vali omits this field; it defaults to 0, which is what GeoGuessr expects.</summary>
    public double pitch { get; init; }

    public string? panoId { get; init; }
}
```

- [x] **Step 5: Write `DescriptionTemplate`**

`src/GeoVali/Maps/DescriptionTemplate.cs`:

```csharp
using System.Globalization;

namespace GeoVali.Maps;

/// <summary>
/// Expands tokens in a map description at publish time. The token, not the substituted text, is
/// what stays in <c>geoguessr.json</c>, so this is never applied before writing the file back.
/// </summary>
public static class DescriptionTemplate
{
    public const string LocationCountToken = "{{LocationCount}}";

    public static string Expand(string description, int locationCount) =>
        string.IsNullOrEmpty(description)
            ? string.Empty
            : description.Replace(LocationCountToken, locationCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
```

- [x] **Step 6: Write `MapMetadataStore`**

`src/GeoVali/Maps/MapMetadataStore.cs`:

```csharp
using System.Text.Json;

namespace GeoVali.Maps;

/// <summary>Reads and writes the two JSON files that live beside a map definition.</summary>
public static class MapMetadataStore
{
    private static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };

    /// <summary>Returns null when the map has no <c>geoguessr.json</c>, i.e. is not set up yet.</summary>
    public static async Task<GeoguessrMetadata?> ReadMetadataAsync(string directory)
    {
        var path = MapPaths.Metadata(directory);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GeoguessrMetadata>(stream, Read);
    }

    public static async Task WriteMetadataAsync(string directory, GeoguessrMetadata metadata)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(MapPaths.Metadata(directory), JsonSerializer.Serialize(metadata, Write));
    }

    /// <summary>
    /// Never throws. A missing or corrupt ephemeral file means "never published", which makes the
    /// map due — the safe direction, since the worst case is one redundant regeneration.
    /// </summary>
    public static async Task<EphemeralMetadata> ReadEphemeralAsync(string directory)
    {
        var path = MapPaths.Ephemeral(directory);
        if (!File.Exists(path))
        {
            return new EphemeralMetadata();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<EphemeralMetadata>(stream, Read) ?? new EphemeralMetadata();
        }
        catch (JsonException)
        {
            return new EphemeralMetadata();
        }
    }

    public static async Task WriteEphemeralAsync(string directory, EphemeralMetadata ephemeral)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(MapPaths.Ephemeral(directory), JsonSerializer.Serialize(ephemeral, Write));
    }
}
```

- [x] **Step 7: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MapMetadataStoreTests|FullyQualifiedName~DescriptionTemplateTests"`
Expected: PASS, 13 tests.

- [x] **Step 8: Commit**

```bash
git add src/GeoVali/Maps/MapModels.cs src/GeoVali/Maps/MapMetadataStore.cs \
        src/GeoVali/Maps/DescriptionTemplate.cs \
        tests/GeoVali.Tests/MapMetadataStoreTests.cs tests/GeoVali.Tests/DescriptionTemplateTests.cs
git commit -m "feat: read and write geoguessr.json and geoguessr.ephemeral.json"
```

---

### Task 5: Reading locations and the minimum-location guard

**Files:**
- Create: `src/GeoVali/Maps/LocationFile.cs`
- Create: `src/GeoVali/Maps/MapPublishGuard.cs`
- Test: `tests/GeoVali.Tests/LocationFileTests.cs`
- Test: `tests/GeoVali.Tests/MapPublishGuardTests.cs`

**Interfaces:**
- Consumes: `MapPaths`, `ValiLocation` (Tasks 3–4).
- Produces:
  - `GeoVali.Maps.LocationFile.ReadAsync(string directory, CancellationToken ct)` → `Task<IReadOnlyList<ValiLocation>>`.
  - `GeoVali.Maps.MapPublishGuard.MinimumLocationCount` → `const int` = `5`.
  - `GeoVali.Maps.MapPublishGuard.EnsurePublishable(string mapName, int locationCount)` → `void`, throws `GeoVali.Maps.NotPublishableException`.
  - `GeoVali.Maps.NotPublishableException : Exception`.

`map-locations.json` for a large map is a bare JSON array of tens of thousands of objects and can exceed 13 MB, so read it from a `FileStream` rather than `File.ReadAllTextAsync`.

- [x] **Step 1: Write the failing tests**

`tests/GeoVali.Tests/LocationFileTests.cs`:

```csharp
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class LocationFileTests
{
    // Trimmed from a real vali map-locations.json: a bare array, extra fields that
    // GeoVali does not care about, and no "pitch" property at all.
    private const string ValiOutput = """
        [{"lat":-19.36172016699375,"lng":25.87176560604501,"heading":153,"extra":{"tags":["2012"]},
          "panoId":"rJ_gIkM2XxRPedayIlzlBw","countryCode":"BW","subdivisionCode":"BW-CE",
          "locationId":"3870162097","resolutionHeight":6656,"year":2012,"month":4},
         {"lat":-32.43315093026216,"lng":24.627465821100692,"heading":341,
          "panoId":"35_3U7bbl8IW_05SV2WmCA","countryCode":"ZA"}]
        """;

    [Fact]
    public async Task Reads_the_fields_geoguessr_needs_and_ignores_the_rest()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), ValiOutput);

        var locations = await LocationFile.ReadAsync(dir, CancellationToken.None);

        Assert.Equal(2, locations.Count);
        Assert.Equal(-19.36172016699375, locations[0].lat);
        Assert.Equal(25.87176560604501, locations[0].lng);
        Assert.Equal(153, locations[0].heading);
        Assert.Equal("rJ_gIkM2XxRPedayIlzlBw", locations[0].panoId);
    }

    [Fact]
    public async Task Defaults_pitch_to_zero_because_vali_does_not_write_it()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("africa");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), ValiOutput);

        var locations = await LocationFile.ReadAsync(dir, CancellationToken.None);

        Assert.All(locations, location => Assert.Equal(0, location.pitch));
    }

    [Fact]
    public async Task Returns_empty_when_vali_produced_no_file()
    {
        using var temp = new TempDir();
        var locations = await LocationFile.ReadAsync(temp.Dir("never-generated"), CancellationToken.None);
        Assert.Empty(locations);
    }

    [Fact]
    public async Task Reports_a_corrupt_file_as_a_readable_error()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("truncated");
        await File.WriteAllTextAsync(MapPaths.Locations(dir), "[{\"lat\":1,\"lng\":2");

        var exception = await Assert.ThrowsAsync<NotPublishableException>(
            () => LocationFile.ReadAsync(dir, CancellationToken.None));

        Assert.Contains("map-locations.json", exception.Message);
        Assert.Contains("could not be read", exception.Message);
    }
}
```

`tests/GeoVali.Tests/MapPublishGuardTests.cs`:

```csharp
using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class MapPublishGuardTests
{
    [Fact]
    public void Minimum_is_five_locations()
    {
        Assert.Equal(5, MapPublishGuard.MinimumLocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Refuses_to_publish_a_map_with_too_few_locations(int count)
    {
        var exception = Assert.Throws<NotPublishableException>(
            () => MapPublishGuard.EnsurePublishable("Coastal Sri Lanka", count));

        // GeoGuessr answers a bare 400 here, which is unreadable in a summary.
        // Say what is actually wrong instead.
        Assert.Contains("Coastal Sri Lanka", exception.Message);
        Assert.Contains(count.ToString(), exception.Message);
        Assert.Contains("at least 5", exception.Message);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(55000)]
    public void Allows_a_map_with_enough_locations(int count)
    {
        MapPublishGuard.EnsurePublishable("An Arbitrary Africa", count);
    }
}
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LocationFileTests|FullyQualifiedName~MapPublishGuardTests"`
Expected: FAIL — `LocationFile` and `MapPublishGuard` do not exist.

- [x] **Step 3: Write `MapPublishGuard`**

`src/GeoVali/Maps/MapPublishGuard.cs`:

```csharp
namespace GeoVali.Maps;

/// <summary>A map cannot be published, with a message written for the user rather than for a log.</summary>
public sealed class NotPublishableException(string message) : Exception(message);

/// <summary>
/// GeoGuessr rejects a map with fewer than <see cref="MinimumLocationCount"/> coordinates with a
/// bare 400, which surfaces in the dashboard as an unreadable "Bad Request". Stop before the
/// request and say what is actually wrong instead.
/// </summary>
public static class MapPublishGuard
{
    public const int MinimumLocationCount = 5;

    public static void EnsurePublishable(string mapName, int locationCount)
    {
        if (locationCount < MinimumLocationCount)
        {
            throw new NotPublishableException(
                $"Refusing to publish \"{mapName}\": vali produced {locationCount} locations, " +
                $"and GeoGuessr needs at least {MinimumLocationCount}. " +
                "Widen the map definition in map.json and try again.");
        }
    }
}
```

- [x] **Step 4: Write `LocationFile`**

`src/GeoVali/Maps/LocationFile.cs`:

```csharp
using System.Text.Json;

namespace GeoVali.Maps;

/// <summary>
/// Reads <c>map-locations.json</c>. A large map's file is a bare JSON array of tens of thousands
/// of objects and runs to double-digit megabytes, so it is streamed rather than read into a string.
/// </summary>
public static class LocationFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static async Task<IReadOnlyList<ValiLocation>> ReadAsync(string directory, CancellationToken ct)
    {
        var path = MapPaths.Locations(directory);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            return await JsonSerializer.DeserializeAsync<List<ValiLocation>>(stream, Options, ct) ?? [];
        }
        catch (JsonException e)
        {
            throw new NotPublishableException(
                $"{MapPaths.LocationsFileName} in {directory} could not be read as JSON " +
                $"({e.Message}). Delete it and run this map again to regenerate it.");
        }
    }
}
```

- [x] **Step 5: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LocationFileTests|FullyQualifiedName~MapPublishGuardTests"`
Expected: PASS, 10 tests.

- [x] **Step 6: Commit**

```bash
git add src/GeoVali/Maps/LocationFile.cs src/GeoVali/Maps/MapPublishGuard.cs \
        tests/GeoVali.Tests/LocationFileTests.cs tests/GeoVali.Tests/MapPublishGuardTests.cs
git commit -m "feat: stream map-locations.json and guard the minimum location count"
```

---

### Task 6: GeoguessrClient — the four-call publish sequence

**Files:**
- Create: `src/GeoVali/Geoguessr/IGeoguessrClient.cs`
- Create: `src/GeoVali/Geoguessr/GeoguessrClient.cs`
- Create: `src/GeoVali/Geoguessr/AvatarGenerator.cs`
- Create: `src/GeoVali/Geoguessr/MapUrlParser.cs`
- Test: `tests/GeoVali.Tests/Support/RecordingHandler.cs`
- Test: `tests/GeoVali.Tests/GeoguessrClientTests.cs`
- Test: `tests/GeoVali.Tests/MapUrlParserTests.cs`

**Interfaces:**
- Consumes: `MapAvatar`, `ValiLocation` (Task 4).
- Produces:
  - `GeoVali.Geoguessr.IGeoguessrClient` with
    `Task<string?> GetSignedInUserNickAsync(CancellationToken ct)` (null on 401),
    `Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct)` (null when missing or not owned),
    `Task<string> PublishAsync(PublishRequest request, CancellationToken ct)` (returns the map id).
  - `GeoVali.Geoguessr.DraftInfo` — `sealed record DraftInfo(string id, string name, int version)`.
  - `GeoVali.Geoguessr.PublishRequest` — `sealed record PublishRequest(string? MapId, string Name, string Description, MapAvatar Avatar, bool Publish, IReadOnlyList<ValiLocation> Locations)`.
  - `GeoVali.Geoguessr.GeoguessrAuthException : Exception` and `GeoVali.Geoguessr.GeoguessrException : Exception`.
  - `GeoVali.Geoguessr.GeoguessrClient.BaseAddress` → `const string` = `"https://www.geoguessr.com/"`.
  - `GeoVali.Geoguessr.AvatarGenerator.Generate()` → `MapAvatar`.
  - `GeoVali.Geoguessr.MapUrlParser.TryExtractMapId(string input, out string mapId)` → `bool`.
  - Test helper `GeoVali.Tests.Support.RecordingHandler`.

The read-then-write-incremented-version step is mandatory: the API rejects a stale version, and getting it wrong fails in ways that are hard to diagnose. That is what the first test below pins down.

- [x] **Step 1: Write the recording HTTP handler**

`tests/GeoVali.Tests/Support/RecordingHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace GeoVali.Tests.Support;

/// <summary>
/// Stands in for the GeoGuessr API. Records every call in order and returns queued responses,
/// so a test can assert the exact request sequence and the bodies that were sent.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public sealed record Call(HttpMethod Method, string PathAndQuery, string Body, string? CookieHeader);

    public List<Call> Calls { get; } = [];

    public RecordingHandler Enqueue(HttpStatusCode status, string json = "{}")
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues("Cookie", out var cookies);
        Calls.Add(new Call(request.Method, request.RequestUri!.PathAndQuery, body, cookies?.FirstOrDefault()));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
```

- [x] **Step 2: Write the failing client tests**

`tests/GeoVali.Tests/GeoguessrClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class GeoguessrClientTests
{
    private static readonly MapAvatar Avatar = new()
    {
        background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese"
    };

    private static readonly IReadOnlyList<ValiLocation> FiveLocations =
    [
        new() { lat = 1, lng = 2, heading = 10, panoId = "pano-a" },
        new() { lat = 3, lng = 4, heading = 20, panoId = "pano-b" },
        new() { lat = 5, lng = 6, heading = 30, panoId = "" },
        new() { lat = 7, lng = 8, heading = 40, panoId = null },
        new() { lat = 9, lng = 10, heading = 50, panoId = "pano-e" }
    ];

    private static (GeoguessrClient client, RecordingHandler handler) Build(string cookie = "cookie-value")
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(GeoguessrClient.BaseAddress) };
        http.DefaultRequestHeaders.Add("Cookie", $"_ncfa={cookie}");
        return (new GeoguessrClient(http), handler);
    }

    [Fact]
    public async Task Existing_map_makes_exactly_three_calls_in_order_and_puts_version_plus_one()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"Coastal Sri Lanka","version":41}""") // GET draft
               .Enqueue(HttpStatusCode.OK)                                                                // PUT draft
               .Enqueue(HttpStatusCode.OK);                                                               // PUT publish

        var id = await client.PublishAsync(
            new PublishRequest("map-1", "Coastal Sri Lanka", "1234 locations.", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.Equal("map-1", id);
        Assert.Equal(3, handler.Calls.Count);

        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1", handler.Calls[0].PathAndQuery);

        Assert.Equal(HttpMethod.Put, handler.Calls[1].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1", handler.Calls[1].PathAndQuery);

        Assert.Equal(HttpMethod.Put, handler.Calls[2].Method);
        Assert.Equal("/api/v4/user-maps/drafts/map-1/publish", handler.Calls[2].PathAndQuery);

        // The one quirk that is silent when wrong: version must be the read value plus one.
        var put = JsonNode.Parse(handler.Calls[1].Body)!;
        Assert.Equal(42, put["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task New_map_creates_a_draft_first_making_four_calls()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"new-map"}""")                          // POST drafts
               .Enqueue(HttpStatusCode.OK, """{"id":"new-map","name":"Fresh","version":0}""") // GET draft
               .Enqueue(HttpStatusCode.OK)                                                    // PUT draft
               .Enqueue(HttpStatusCode.OK);                                                   // PUT publish

        var id = await client.PublishAsync(
            new PublishRequest(null, "Fresh", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.Equal("new-map", id);
        Assert.Equal(4, handler.Calls.Count);

        Assert.Equal(HttpMethod.Post, handler.Calls[0].Method);
        Assert.Equal("/api/v4/user-maps/drafts", handler.Calls[0].PathAndQuery);
        var created = JsonNode.Parse(handler.Calls[0].Body)!;
        Assert.Equal("Fresh", created["name"]!.GetValue<string>());
        Assert.Equal("coordinates", created["mode"]!.GetValue<string>());

        var put = JsonNode.Parse(handler.Calls[2].Body)!;
        Assert.Equal(1, put["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task Unpublished_map_updates_the_draft_but_never_calls_publish()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"Draft only","version":3}""")
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "Draft only", "d", Avatar, Publish: false, FiveLocations),
            CancellationToken.None);

        Assert.Equal(2, handler.Calls.Count);
        Assert.DoesNotContain(handler.Calls, c => c.PathAndQuery.EndsWith("/publish"));
    }

    [Fact]
    public async Task Maps_locations_to_custom_coordinates_and_omits_an_empty_pano_id()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        var coordinates = JsonNode.Parse(handler.Calls[1].Body)!["customCoordinates"]!.AsArray();
        Assert.Equal(5, coordinates.Count);
        Assert.Equal(1, coordinates[0]!["lat"]!.GetValue<double>());
        Assert.Equal(2, coordinates[0]!["lng"]!.GetValue<double>());
        Assert.Equal(10, coordinates[0]!["heading"]!.GetValue<double>());
        Assert.Equal(0, coordinates[0]!["pitch"]!.GetValue<double>());
        Assert.Equal("pano-a", coordinates[0]!["panoId"]!.GetValue<string>());

        // An empty or missing panoId is sent as null, not as "".
        Assert.Null(coordinates[2]!["panoId"]?.GetValue<string>());
        Assert.Null(coordinates[3]!["panoId"]?.GetValue<string>());
    }

    [Fact]
    public async Task Sends_the_name_description_avatar_and_published_flag()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "Coastal Sri Lanka", "1234 hand-picked coastal locations.",
                Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        var body = JsonNode.Parse(handler.Calls[1].Body)!;
        Assert.Equal("map-1", body["id"]!.GetValue<string>());
        Assert.Equal("Coastal Sri Lanka", body["name"]!.GetValue<string>());
        Assert.Equal("1234 hand-picked coastal locations.", body["description"]!.GetValue<string>());
        Assert.True(body["published"]!.GetValue<bool>());
        Assert.Equal("evening", body["avatar"]!["background"]!.GetValue<string>());
        Assert.Equal("skyline", body["avatar"]!["landscape"]!.GetValue<string>());
        Assert.Equal("yellow", body["avatar"]!["ground"]!.GetValue<string>());
        Assert.Equal("japanese", body["avatar"]!["decoration"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sends_the_ncfa_cookie_on_every_call()
    {
        var (client, handler) = Build("secret-cookie");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.OK)
               .Enqueue(HttpStatusCode.OK);

        await client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None);

        Assert.All(handler.Calls, call => Assert.Equal("_ncfa=secret-cookie", call.CookieHeader));
    }

    [Fact]
    public async Task A_401_during_publish_raises_the_auth_exception()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<GeoguessrAuthException>(() => client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_put_reports_the_status_and_the_response_body()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-1","name":"n","version":0}""")
               .Enqueue(HttpStatusCode.BadRequest, """{"message":"Too few coordinates"}""");

        var exception = await Assert.ThrowsAsync<GeoguessrException>(() => client.PublishAsync(
            new PublishRequest("map-1", "n", "d", Avatar, Publish: true, FiveLocations),
            CancellationToken.None));

        Assert.Contains("400", exception.Message);
        Assert.Contains("Too few coordinates", exception.Message);
    }

    [Fact]
    public async Task Auth_probe_returns_the_signed_in_nick()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"user":{"nick":"slashP","id":"u1"}}""");

        var nick = await client.GetSignedInUserNickAsync(CancellationToken.None);

        Assert.Equal("slashP", nick);
        Assert.Equal("/api/v3/profiles", handler.Calls.Single().PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Calls.Single().Method);
    }

    [Fact]
    public async Task Auth_probe_returns_null_for_an_expired_cookie()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        Assert.Null(await client.GetSignedInUserNickAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Get_draft_yields_the_name_for_the_link_existing_flow()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"map-9","name":"An Arbitrary Africa","version":306}""");

        var draft = await client.GetDraftAsync("map-9", CancellationToken.None);

        Assert.Equal("An Arbitrary Africa", draft!.name);
        Assert.Equal(306, draft.version);
    }

    [Fact]
    public async Task Get_draft_returns_null_when_the_user_does_not_own_the_map()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        Assert.Null(await client.GetDraftAsync("someone-elses-map", CancellationToken.None));
    }
}
```

`tests/GeoVali.Tests/MapUrlParserTests.cs`:

```csharp
using GeoVali.Geoguessr;
using Xunit;

namespace GeoVali.Tests;

public class MapUrlParserTests
{
    [Theory]
    [InlineData("https://www.geoguessr.com/maps/60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    [InlineData("https://www.geoguessr.com/maps/60a19170089ecc0001db6609/play", "60a19170089ecc0001db6609")]
    [InlineData("https://www.geoguessr.com/map-maker/60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    [InlineData("http://geoguessr.com/maps/60a19170089ecc0001db6609?x=1", "60a19170089ecc0001db6609")]
    [InlineData("  https://www.geoguessr.com/maps/60a19170089ecc0001db6609  ", "60a19170089ecc0001db6609")]
    [InlineData("60a19170089ecc0001db6609", "60a19170089ecc0001db6609")]
    public void Extracts_the_map_id(string input, string expected)
    {
        Assert.True(MapUrlParser.TryExtractMapId(input, out var mapId));
        Assert.Equal(expected, mapId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.geoguessr.com/")]
    [InlineData("https://www.geoguessr.com/maps/")]
    [InlineData("not a url at all")]
    [InlineData("https://example.com/maps/60a19170089ecc0001db6609")]
    public void Rejects_input_that_is_not_a_geoguessr_map(string input)
    {
        Assert.False(MapUrlParser.TryExtractMapId(input, out var mapId));
        Assert.Equal(string.Empty, mapId);
    }
}
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GeoguessrClientTests|FullyQualifiedName~MapUrlParserTests"`
Expected: FAIL — `GeoguessrClient` / `MapUrlParser` do not exist.

- [x] **Step 4: Write the interface and DTOs**

`src/GeoVali/Geoguessr/IGeoguessrClient.cs`:

```csharp
using GeoVali.Maps;

namespace GeoVali.Geoguessr;

/// <summary>The GeoGuessr session cookie is no longer valid. Aborts the whole run, never one map.</summary>
public sealed class GeoguessrAuthException()
    : Exception("Your GeoGuessr sign-in has expired. Open Settings and paste a fresh _ncfa cookie value.");

/// <summary>A GeoGuessr call failed, with the status and response text folded into the message.</summary>
public sealed class GeoguessrException(string message) : Exception(message);

/// <summary>What <c>GET /api/v4/user-maps/drafts/{id}</c> tells us.</summary>
public sealed record DraftInfo(string id, string name, int version);

/// <summary>Everything needed for one publish. <paramref name="MapId"/> is null for a brand-new map.</summary>
public sealed record PublishRequest(
    string? MapId,
    string Name,
    string Description,
    MapAvatar Avatar,
    bool Publish,
    IReadOnlyList<ValiLocation> Locations);

public interface IGeoguessrClient
{
    /// <summary>Auth probe. Returns the signed-in nick, or null when the cookie is rejected.</summary>
    Task<string?> GetSignedInUserNickAsync(CancellationToken ct);

    /// <summary>Returns null when the draft does not exist or the signed-in user does not own it.</summary>
    Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct);

    /// <summary>Runs the create/read/update/publish sequence. Returns the map id.</summary>
    Task<string> PublishAsync(PublishRequest request, CancellationToken ct);
}
```

- [x] **Step 5: Write `GeoguessrClient`**

`src/GeoVali/Geoguessr/GeoguessrClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeoVali.Maps;

namespace GeoVali.Geoguessr;

public sealed class GeoguessrClient(HttpClient http) : IGeoguessrClient
{
    public const string BaseAddress = "https://www.geoguessr.com/";

    private static readonly JsonSerializerOptions Body = new() { WriteIndented = false };

    public async Task<string?> GetSignedInUserNickAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/v3/profiles", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        await EnsureSuccess(response, "reading your GeoGuessr profile", ct);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return node?["user"]?["nick"]?.GetValue<string>() ?? node?["nick"]?.GetValue<string>();
    }

    public async Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/v4/user-maps/drafts/{mapId}", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new GeoguessrAuthException();
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
                   ?? throw new GeoguessrException($"GeoGuessr returned an empty response for map {mapId}.");

        return new DraftInfo(
            node["id"]?.GetValue<string>() ?? mapId,
            node["name"]?.GetValue<string>() ?? "",
            node["version"]?.GetValue<int>() ?? 0);
    }

    public async Task<string> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        var mapId = request.MapId;

        if (string.IsNullOrEmpty(mapId))
        {
            using var created = await http.PostAsJsonAsync(
                "api/v4/user-maps/drafts",
                new { name = request.Name, mode = "coordinates" },
                Body, ct);
            await EnsureSuccess(created, $"creating the map \"{request.Name}\"", ct);

            mapId = JsonNode.Parse(await created.Content.ReadAsStringAsync(ct))?["id"]?.GetValue<string>()
                    ?? throw new GeoguessrException(
                        $"GeoGuessr created a draft for \"{request.Name}\" but did not return its id.");
        }

        // Mandatory: the API rejects a stale version, so read the current one and write back +1.
        var draft = await GetDraftAsync(mapId, ct)
                    ?? throw new GeoguessrException(
                        $"GeoGuessr has no map {mapId} on your account. " +
                        "Check the id in geoguessr.json, or clear it to create a new map.");

        var body = new DraftBody
        {
            id = mapId,
            name = request.Name,
            description = request.Description,
            published = request.Publish,
            avatar = new AvatarBody
            {
                background = request.Avatar.background,
                landscape = request.Avatar.landscape,
                ground = request.Avatar.ground,
                decoration = request.Avatar.decoration
            },
            customCoordinates = request.Locations.Select(location => new CoordinateBody
            {
                lat = location.lat,
                lng = location.lng,
                heading = location.heading,
                pitch = location.pitch,
                panoId = string.IsNullOrEmpty(location.panoId) ? null : location.panoId
            }).ToList(),
            version = draft.version + 1
        };

        using (var put = await http.PutAsJsonAsync($"api/v4/user-maps/drafts/{mapId}", body, Body, ct))
        {
            await EnsureSuccess(put, $"updating the map \"{request.Name}\"", ct);
        }

        if (request.Publish)
        {
            using var publish = await http.PutAsJsonAsync(
                $"api/v4/user-maps/drafts/{mapId}/publish", new { }, Body, ct);
            await EnsureSuccess(publish, $"publishing the map \"{request.Name}\"", ct);
        }

        return mapId;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new GeoguessrAuthException();
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        var trimmed = text.Length > 500 ? text[..500] + "..." : text;
        throw new GeoguessrException(
            $"GeoGuessr rejected {what}: {(int)response.StatusCode} {response.ReasonPhrase}. {trimmed}".Trim());
    }

    // The request shape GeoGuessr expects. "highlighted" and the custom-error-distance pair are
    // slashP-specific in the source pipeline; GeoVali always sends their neutral values, but the
    // fields have to be present.
    private sealed record DraftBody
    {
        public string? id { get; init; }
        public bool highlighted { get; init; }
        public string name { get; init; } = "";
        public string description { get; init; } = "";
        public AvatarBody avatar { get; init; } = new();
        public bool published { get; init; }
        public List<CoordinateBody> customCoordinates { get; init; } = [];
        public string[] tags { get; init; } = [];
        public bool hasCustomErrorDistance { get; init; }
        public int maxErrorDistance { get; init; }
        public int version { get; init; }
    }

    private sealed record AvatarBody
    {
        public string background { get; init; } = "";
        public string landscape { get; init; } = "";
        public string ground { get; init; } = "";
        public string decoration { get; init; } = "";
    }

    private sealed record CoordinateBody
    {
        public double lat { get; init; }
        public double lng { get; init; }
        public double heading { get; init; }
        public double pitch { get; init; }
        public string? panoId { get; init; }
    }
}
```

- [x] **Step 6: Write `AvatarGenerator`**

The option lists are GeoGuessr's, copied from the author's `GenerateRandomAvatar`.

`src/GeoVali/Geoguessr/AvatarGenerator.cs`:

```csharp
using GeoVali.Maps;

namespace GeoVali.Geoguessr;

/// <summary>
/// Picks a random map thumbnail. Called once when a map is set up; the result is persisted in
/// geoguessr.json so the thumbnail is stable across runs.
/// </summary>
public static class AvatarGenerator
{
    private static readonly string[] Backgrounds =
        ["sunset", "evening", "night", "sunrise", "day", "darknight", "morning"];

    private static readonly string[] Decorations =
        ["none", "tractor", "smalltrees", "oaktrees", "cactus", "palmtrees", "japanese"];

    private static readonly string[] Grounds =
        ["beige", "blue", "green", "yellow", "darkbrown", "water"];

    private static readonly string[] Landscapes =
    [
        "forest", "houses", "mountains", "snowmountains", "grassmountains", "skyline",
        "hills", "desserthills", "mountaintrees", "volcano", "fuji"
    ];

    public static MapAvatar Generate() => new()
    {
        background = Backgrounds[Random.Shared.Next(Backgrounds.Length)],
        decoration = Decorations[Random.Shared.Next(Decorations.Length)],
        ground = Grounds[Random.Shared.Next(Grounds.Length)],
        landscape = Landscapes[Random.Shared.Next(Landscapes.Length)]
    };
}
```

- [x] **Step 7: Write `MapUrlParser`**

`src/GeoVali/Geoguessr/MapUrlParser.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GeoVali.Geoguessr;

/// <summary>
/// Pulls a map id out of whatever the user pasted: a play link, a map-maker link, or the bare id.
/// </summary>
public static partial class MapUrlParser
{
    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex BareId();

    [GeneratedRegex(@"^/(?:maps|map-maker)/([0-9a-fA-F]{24})(?:/.*)?$")]
    private static partial Regex UrlPath();

    public static bool TryExtractMapId(string input, [NotNullWhen(true)] out string mapId)
    {
        mapId = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (BareId().IsMatch(trimmed))
        {
            mapId = trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith("geoguessr.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = UrlPath().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        mapId = match.Groups[1].Value;
        return true;
    }
}
```

- [x] **Step 8: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GeoguessrClientTests|FullyQualifiedName~MapUrlParserTests"`
Expected: PASS, 24 tests.

- [x] **Step 9: Commit**

```bash
git add src/GeoVali/Geoguessr tests/GeoVali.Tests/GeoguessrClientTests.cs \
        tests/GeoVali.Tests/MapUrlParserTests.cs tests/GeoVali.Tests/Support/RecordingHandler.cs
git commit -m "feat: GeoGuessr publish sequence with the mandatory version increment"
```

---

### Task 7: Bounded retry on transient publish failures

**Files:**
- Create: `src/GeoVali/Geoguessr/TransientRetryHandler.cs`
- Test: `tests/GeoVali.Tests/TransientRetryHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (a plain `DelegatingHandler`).
- Produces: `GeoVali.Geoguessr.TransientRetryHandler` — `public TransientRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null)`, `public const int MaxRetries = 3`.

Two rules that matter and are easy to get wrong: retry only what is safe to repeat (never `POST`, which would create a duplicate draft), and never retry a `401` — that is a whole-run abort, not a blip.

- [x] **Step 1: Write the failing tests**

`tests/GeoVali.Tests/TransientRetryHandlerTests.cs`:

```csharp
using System.Net;
using GeoVali.Geoguessr;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class TransientRetryHandlerTests
{
    private static (HttpClient client, RecordingHandler inner, List<TimeSpan> delays) Build()
    {
        var inner = new RecordingHandler();
        var delays = new List<TimeSpan>();
        var retry = new TransientRetryHandler((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        })
        {
            InnerHandler = inner
        };

        return (new HttpClient(retry) { BaseAddress = new Uri("https://www.geoguessr.com/") }, inner, delays);
    }

    [Fact]
    public async Task Retries_a_server_error_and_succeeds()
    {
        var (client, inner, delays) = Build();
        inner.Enqueue(HttpStatusCode.BadGateway)
             .Enqueue(HttpStatusCode.ServiceUnavailable)
             .Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task Gives_up_after_three_retries()
    {
        var (client, inner, delays) = Build();
        for (var i = 0; i < 5; i++)
        {
            inner.Enqueue(HttpStatusCode.BadGateway);
        }

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(4, inner.Calls.Count); // one attempt plus MaxRetries
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Treats_timeouts_throttling_and_server_errors_as_transient(HttpStatusCode status)
    {
        var (client, inner, _) = Build();
        inner.Enqueue(status).Enqueue(HttpStatusCode.OK);

        var response = await client.GetAsync("api/v4/user-maps/drafts/map-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Never_retries_a_401_because_that_aborts_the_whole_run()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK);

        var response = await client.GetAsync("api/v3/profiles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Never_retries_a_400_because_the_request_is_wrong_not_unlucky()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.BadRequest).Enqueue(HttpStatusCode.OK);

        var response = await client.PutAsync("api/v4/user-maps/drafts/map-1", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Never_retries_a_post_because_it_would_create_a_second_draft()
    {
        var (client, inner, _) = Build();
        inner.Enqueue(HttpStatusCode.BadGateway).Enqueue(HttpStatusCode.OK);

        var response = await client.PostAsync("api/v4/user-maps/drafts", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(inner.Calls);
    }
}
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TransientRetryHandlerTests`
Expected: FAIL — `TransientRetryHandler` does not exist.

- [x] **Step 3: Write the handler**

`src/GeoVali/Geoguessr/TransientRetryHandler.cs`:

```csharp
using System.Net;

namespace GeoVali.Geoguessr;

/// <summary>
/// Retries a transient GeoGuessr failure a few times with exponential backoff.
/// Two deliberate exclusions: <c>POST</c> is never retried, because a lost response to
/// <c>POST /drafts</c> followed by a retry would leave the user with two maps; and 401 is never
/// retried, because an expired cookie aborts the whole run rather than being a blip.
/// </summary>
public sealed class TransientRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null) : DelegatingHandler
{
    public const int MaxRetries = 3;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((duration, ct) => Task.Delay(duration, ct));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException e)
            {
                transportFailure = e;
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                // The HttpClient timeout fired, not the caller's cancellation.
                transportFailure = e;
            }

            if (attempt >= MaxRetries || request.Method == HttpMethod.Post)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            response?.Dispose();
            await _delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            attempt++;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)status >= 500;
}
```

- [x] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TransientRetryHandlerTests`
Expected: PASS, 8 tests.

Note: `HttpRequestMessage` cannot be sent twice when it carries a stream body. Every GeoVali request body is created from an in-memory object by `PutAsJsonAsync`, whose content is buffered and re-readable, so replay is safe. Do not add streaming request bodies without revisiting this.

- [x] **Step 5: Commit**

```bash
git add src/GeoVali/Geoguessr/TransientRetryHandler.cs tests/GeoVali.Tests/TransientRetryHandlerTests.cs
git commit -m "feat: bounded retry with backoff on transient GeoGuessr failures"
```

---

### Task 8: ValiRunner — invoking vali and streaming its output

**Files:**
- Create: `src/GeoVali/Vali/IValiRunner.cs`
- Create: `src/GeoVali/Vali/ValiRunner.cs`
- Test: `tests/GeoVali.Tests/ValiRunnerTests.cs`
- Test: `tests/GeoVali.Tests/Support/FakeValiExecutable.cs`

**Interfaces:**
- Consumes: `MapPaths` (Task 3), `TempDir` (Task 3).
- Produces:
  - `GeoVali.Vali.IValiRunner` with `string? FindExecutable()` and
    `Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)`.
  - `GeoVali.Vali.ValiResult` — `sealed record ValiResult(int ExitCode, string LastOutputLine)`.
  - `GeoVali.Vali.ValiNotFoundException : Exception`.
  - `GeoVali.Vali.ValiRunner` — `public ValiRunner(string? executableOverride = null)`.
  - `GeoVali.Vali.ValiRunner.InstallCommand` → `const string` = `"dotnet tool install -g vali"`.
  - `GeoVali.Vali.ValiRunner.StripAnsi(string line)` → `string`.
  - Test helper `GeoVali.Tests.Support.FakeValiExecutable.Create(TempDir temp, string locationsJson, int exitCode = 0, string stdout = "...")` → `string` (path to the script).

vali prints ANSI colour codes — its banner is blue, its "Download/data folder" line green. Those must be stripped before the text reaches the dashboard, or the user sees escape gibberish. In the tests below the escape byte is written as the C# escape `\u001b` rather than pasted literally.

- [x] **Step 1: Write the fake vali executable helper**

`tests/GeoVali.Tests/Support/FakeValiExecutable.cs`:

```csharp
using System.Runtime.InteropServices;

namespace GeoVali.Tests.Support;

/// <summary>
/// Writes a script that behaves like <c>vali generate</c>: prints a line, drops a canned
/// map-locations.json next to the map.json it was pointed at, and exits with a chosen code.
/// Lets the runner and the end-to-end test exercise real process invocation without real vali.
/// </summary>
public static class FakeValiExecutable
{
    /// <returns>The full path to the executable script.</returns>
    public static string Create(TempDir temp, string locationsJson, int exitCode = 0, string stdout = "Generated locations.")
    {
        var payloadPath = Path.Combine(temp.Path, $"canned-{Guid.NewGuid():N}.json");
        File.WriteAllText(payloadPath, locationsJson);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmdPath = Path.Combine(temp.Path, $"fake-vali-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(cmdPath, $"""
                @echo off
                echo {stdout}
                rem args are: generate --file <path to map.json>
                for %%I in ("%~3") do set "MAPDIR=%%~dpI"
                copy /y "{payloadPath}" "%MAPDIR%map-locations.json" >nul
                exit /b {exitCode}
                """);
            return cmdPath;
        }

        var shPath = Path.Combine(temp.Path, $"fake-vali-{Guid.NewGuid():N}.sh");
        File.WriteAllText(shPath, $"""
            #!/bin/sh
            echo "{stdout}"
            # args are: generate --file <path to map.json>
            MAPDIR=$(dirname "$3")
            cp "{payloadPath}" "$MAPDIR/map-locations.json"
            exit {exitCode}
            """.ReplaceLineEndings("\n"));
        File.SetUnixFileMode(shPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shPath;
    }
}
```

- [x] **Step 2: Write the failing tests**

`tests/GeoVali.Tests/ValiRunnerTests.cs`:

```csharp
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
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ValiRunnerTests`
Expected: FAIL — `The type or namespace name 'ValiRunner' could not be found`.

- [x] **Step 4: Write the interface**

`src/GeoVali/Vali/IValiRunner.cs`:

```csharp
namespace GeoVali.Vali;

/// <summary>vali is not installed. Carries the exact command the user needs to run.</summary>
public sealed class ValiNotFoundException(string message) : Exception(message);

/// <summary>Outcome of one <c>vali generate</c>.</summary>
public sealed record ValiResult(int ExitCode, string LastOutputLine);

public interface IValiRunner
{
    /// <summary>Full path to the vali executable, or null when it is not installed.</summary>
    string? FindExecutable();

    /// <summary>
    /// Runs <c>vali generate --file &lt;mapDirectory&gt;/map.json</c>, which writes
    /// <c>map-locations.json</c> into the same directory.
    /// </summary>
    Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct);
}
```

- [x] **Step 5: Write `ValiRunner`**

`src/GeoVali/Vali/ValiRunner.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GeoVali.Maps;

namespace GeoVali.Vali;

/// <param name="executableOverride">
/// An explicit path to vali. Null means "look it up on PATH and in the dotnet tools folder".
/// </param>
public sealed partial class ValiRunner(string? executableOverride = null) : IValiRunner
{
    /// <summary>What GeoVali tells the user to run when vali is missing. It never runs it for them.</summary>
    public const string InstallCommand = "dotnet tool install -g vali";

    // ESC [ ... <letter> — the SGR colour sequences vali wraps its banner in.
    [GeneratedRegex("\u001b\\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiEscape();

    /// <summary>vali prints a coloured banner; the dashboard must not show the escape codes.</summary>
    public static string StripAnsi(string line) => AnsiEscape().Replace(line, string.Empty);

    public string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(executableOverride))
        {
            return File.Exists(executableOverride) ? executableOverride : null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string[] fileNames = isWindows ? ["vali.exe", "vali.cmd", "vali.bat"] : ["vali"];

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"));

        return directories
            .SelectMany(directory => fileNames.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
    }

    public async Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)
    {
        var executable = FindExecutable()
            ?? throw new ValiNotFoundException(
                $"vali was not found on your PATH. Install it with:  {InstallCommand}");

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = mapDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("generate");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(MapPaths.Definition(mapDirectory));

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var lastLine = "";
        void Handle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var line = StripAnsi(raw).TrimEnd();
            if (line.Length == 0)
            {
                return;
            }

            lastLine = line;
            onOutput(line);
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ValiResult(process.ExitCode, lastLine);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ValiRunnerTests`
Expected: PASS, 11 test cases.

- [x] **Step 7: Commit**

```bash
git add src/GeoVali/Vali tests/GeoVali.Tests/ValiRunnerTests.cs tests/GeoVali.Tests/Support/FakeValiExecutable.cs
git commit -m "feat: invoke vali generate and stream its output"
```

---

### Task 9: Config and the protected credential store

**Files:**
- Create: `src/GeoVali/Configuration/AppPaths.cs`
- Create: `src/GeoVali/Configuration/AppConfig.cs`
- Create: `src/GeoVali/Configuration/ConfigStore.cs`
- Create: `src/GeoVali/Configuration/ICredentialProtector.cs`
- Create: `src/GeoVali/Configuration/CredentialStore.cs`
- Modify: `src/GeoVali/GeoVali.csproj` (add the `System.Security.Cryptography.ProtectedData` package)
- Test: `tests/GeoVali.Tests/ConfigStoreTests.cs`
- Test: `tests/GeoVali.Tests/CredentialStoreTests.cs`

**Interfaces:**
- Consumes: `Cadence.DefaultDaysBetweenUpdates` (Task 2), `TempDir` (Task 3).
- Produces:
  - `GeoVali.Configuration.AppPaths` — static `string ConfigDirectory`, `ConfigFile`, `CredentialsFile`, `LogDirectory`.
  - `GeoVali.Configuration.AppConfig` — `sealed record` with `string? mapsRoot`, `int defaultCadenceDays`, `int checkIntervalMinutes`, `int dashboardPort`, `bool startAtLogin`, `string? valiExecutablePath`.
  - `GeoVali.Configuration.ConfigStore` — `public ConfigStore(string directory)`, `AppConfig Read()`, `void Write(AppConfig config)`, `AppConfig Current { get; }`, `const string FileName = "config.json"`.
  - `GeoVali.Configuration.ICredentialProtector` — `byte[] Protect(string plaintext)`, `string? Unprotect(byte[] protectedBytes)`.
  - `GeoVali.Configuration.CredentialProtectorFactory.Create()` → `ICredentialProtector`.
  - `GeoVali.Configuration.CredentialStore` — `public CredentialStore(string directory, ICredentialProtector protector)`, `string? ReadCookie()`, `void WriteCookie(string cookie)`, `void Clear()`, `const string FileName = "credentials.json"`.

- [x] **Step 1: Add the DPAPI package**

```bash
dotnet add src/GeoVali/GeoVali.csproj package System.Security.Cryptography.ProtectedData
```

The package is cross-platform to reference; its functionality is Windows-only, which is exactly why the Unix path uses file permissions instead.

- [x] **Step 2: Write the failing config tests**

`tests/GeoVali.Tests/ConfigStoreTests.cs`:

```csharp
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
```

- [x] **Step 3: Write the failing credential tests**

`tests/GeoVali.Tests/CredentialStoreTests.cs`:

```csharp
using System.Text;
using GeoVali.Configuration;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class CredentialStoreTests
{
    /// <summary>
    /// Stand-in for DPAPI: reversible, but not plaintext, so a test can prove the cookie is not
    /// written verbatim without depending on which OS the suite runs on.
    /// </summary>
    private sealed class ReversingProtector : ICredentialProtector
    {
        public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext).Reverse().ToArray();
        public string? Unprotect(byte[] protectedBytes) => Encoding.UTF8.GetString(protectedBytes.Reverse().ToArray());
    }

    [Fact]
    public void Round_trips_the_cookie()
    {
        using var temp = new TempDir();

        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("ncfa-value-abc123");

        Assert.Equal("ncfa-value-abc123", new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Never_writes_the_cookie_as_plaintext()
    {
        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("ncfa-value-abc123");

        var onDisk = File.ReadAllText(Path.Combine(temp.Path, CredentialStore.FileName));

        Assert.DoesNotContain("ncfa-value-abc123", onDisk);
    }

    [Fact]
    public void Keeps_the_cookie_out_of_config_json()
    {
        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("secret");
        new ConfigStore(temp.Path).Write(new AppConfig { mapsRoot = Path.Combine(temp.Path, "maps") });

        // A diagnostics dump can safely include config.json; it never touches credentials.json.
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(temp.Path, ConfigStore.FileName)));
        Assert.True(File.Exists(Path.Combine(temp.Path, CredentialStore.FileName)));
    }

    [Fact]
    public void Returns_null_before_a_cookie_has_been_pasted()
    {
        using var temp = new TempDir();
        Assert.Null(new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Clear_removes_the_stored_cookie()
    {
        using var temp = new TempDir();
        var store = new CredentialStore(temp.Path, new ReversingProtector());
        store.WriteCookie("secret");

        store.Clear();

        Assert.Null(store.ReadCookie());
    }

    [Fact]
    public void Returns_null_rather_than_throwing_when_the_stored_value_cannot_be_decoded()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, CredentialStore.FileName), """{"ncfa":"not-base64!!"}""");

        Assert.Null(new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Restricts_the_credentials_file_to_the_owner_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows relies on DPAPI CurrentUser scope instead of file permissions.
        }

        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("secret");

        var mode = File.GetUnixFileMode(Path.Combine(temp.Path, CredentialStore.FileName));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
```

- [x] **Step 4: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfigStoreTests|FullyQualifiedName~CredentialStoreTests"`
Expected: FAIL — `AppPaths`, `ConfigStore`, `CredentialStore` do not exist.

- [x] **Step 5: Write `AppPaths`**

`src/GeoVali/Configuration/AppPaths.cs`:

```csharp
namespace GeoVali.Configuration;

/// <summary>
/// Application config lives outside the maps folder, in the platform location, so the maps tree
/// stays exactly what the user committed.
/// </summary>
public static class AppPaths
{
    public static string ConfigDirectory { get; } = Resolve();

    public static string ConfigFile => Path.Combine(ConfigDirectory, ConfigStore.FileName);
    public static string CredentialsFile => Path.Combine(ConfigDirectory, CredentialStore.FileName);
    public static string LogDirectory => Path.Combine(ConfigDirectory, "logs");

    private static string Resolve()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            // %APPDATA%\GeoVali\
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoVali");
        }

        if (OperatingSystem.IsMacOS())
        {
            // ~/Library/Application Support/GeoVali/
            // Not SpecialFolder.ApplicationData, which .NET maps to ~/.config on macOS too.
            return Path.Combine(home, "Library", "Application Support", "GeoVali");
        }

        // $XDG_CONFIG_HOME/geovali/, falling back to ~/.config/geovali/
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg;
        return Path.Combine(root, "geovali");
    }
}
```

- [x] **Step 6: Write `AppConfig` and `ConfigStore`**

`src/GeoVali/Configuration/AppConfig.cs`:

```csharp
using GeoVali.Maps;

namespace GeoVali.Configuration;

/// <summary>Everything in <c>config.json</c>. Never holds the cookie — that lives in credentials.json.</summary>
public sealed record AppConfig
{
    /// <summary>Null until the user has picked a maps folder in the first-run screen.</summary>
    public string? mapsRoot { get; init; }

    public int defaultCadenceDays { get; init; } = Cadence.DefaultDaysBetweenUpdates;
    public int checkIntervalMinutes { get; init; } = 30;
    public int dashboardPort { get; init; } = 5099;
    public bool startAtLogin { get; init; }

    /// <summary>Set only when vali is somewhere the PATH lookup does not find.</summary>
    public string? valiExecutablePath { get; init; }
}
```

`src/GeoVali/Configuration/ConfigStore.cs`:

```csharp
using System.Text.Json;

namespace GeoVali.Configuration;

public sealed class ConfigStore(string directory)
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AppConfig? _cached;

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>The config as last read or written. Reads from disk once, then serves from memory.</summary>
    public AppConfig Current => _cached ??= Read();

    public AppConfig Read()
    {
        if (!File.Exists(FilePath))
        {
            return _cached = new AppConfig();
        }

        try
        {
            _cached = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // A hand-edited config that no longer parses must not stop the tool from starting.
            _cached = new AppConfig();
        }

        return _cached;
    }

    public void Write(AppConfig config)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
        _cached = config;
    }
}
```

- [x] **Step 7: Write the protector and the credential store**

`src/GeoVali/Configuration/ICredentialProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace GeoVali.Configuration;

public interface ICredentialProtector
{
    byte[] Protect(string plaintext);
    string? Unprotect(byte[] protectedBytes);
}

/// <summary>Windows: DPAPI, CurrentUser scope. The bytes are useless to any other account.</summary>
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    public byte[] Protect(string plaintext) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public string? Unprotect(byte[] protectedBytes)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // Written by a different Windows account, or the profile was rebuilt.
            return null;
        }
    }
}

/// <summary>
/// macOS and Linux: no encryption, only the owner-only file permissions applied by
/// <see cref="CredentialStore"/>. This asymmetry is deliberate and is stated to the user in the
/// README and the settings screen. An OS keychain would cost a native dependency per platform,
/// which is not worth it for a session cookie the user can revoke by signing out of GeoGuessr.
/// </summary>
public sealed class PassthroughCredentialProtector : ICredentialProtector
{
    public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext);
    public string? Unprotect(byte[] protectedBytes) => Encoding.UTF8.GetString(protectedBytes);
}

public static class CredentialProtectorFactory
{
    public static ICredentialProtector Create() =>
        OperatingSystem.IsWindows() ? new DpapiCredentialProtector() : new PassthroughCredentialProtector();
}
```

`src/GeoVali/Configuration/CredentialStore.cs`:

```csharp
using System.Text.Json;

namespace GeoVali.Configuration;

/// <summary>
/// Holds only the <c>_ncfa</c> cookie, in its own file, so a diagnostics dump of config.json can
/// never contain it.
/// </summary>
public sealed class CredentialStore(string directory, ICredentialProtector protector)
{
    public const string FileName = "credentials.json";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private string FilePath => Path.Combine(directory, FileName);

    private sealed record StoredCredentials(string? ncfa);

    public string? ReadCookie()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(FilePath));
            if (string.IsNullOrEmpty(stored?.ncfa))
            {
                return null;
            }

            var cookie = protector.Unprotect(Convert.FromBase64String(stored.ncfa));
            return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            // Corrupt, or written by another user account. Treat as "no cookie" and let the
            // dashboard ask for a fresh one, rather than failing to start.
            return null;
        }
    }

    public void WriteCookie(string cookie)
    {
        Directory.CreateDirectory(directory);
        var payload = new StoredCredentials(Convert.ToBase64String(protector.Protect(cookie)));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(payload));
        RestrictToOwner();
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    private void RestrictToOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(FilePath, OwnerOnly);
    }
}
```

- [x] **Step 8: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ConfigStoreTests|FullyQualifiedName~CredentialStoreTests"`
Expected: PASS, 13 tests.

- [x] **Step 9: Commit**

```bash
git add src/GeoVali/Configuration src/GeoVali/GeoVali.csproj \
        tests/GeoVali.Tests/ConfigStoreTests.cs tests/GeoVali.Tests/CredentialStoreTests.cs
git commit -m "feat: platform config directory and a separately protected cookie store"
```

---

### Task 10: RunLog — rolling file, in-memory buffer, and cookie redaction

**Files:**
- Create: `src/GeoVali/Running/RunLog.cs`
- Test: `tests/GeoVali.Tests/RunLogTests.cs`

**Interfaces:**
- Consumes: `TempDir` (Task 3).
- Produces:
  - `GeoVali.Running.LogEntry` — `sealed record LogEntry(DateTime timestampUtc, string level, string message)`.
  - `GeoVali.Running.RunLog` — `public RunLog(string logDirectory, Func<string?> cookieProvider)`,
    `void Info(string message)`, `void Error(string message)`, `IReadOnlyList<LogEntry> Recent()`,
    `event Action<LogEntry>? Appended`, `void PruneOldFiles()`,
    `const int BufferSize = 500`, `const int RetentionDays = 14`.

`cookieProvider` is how the "never logs the cookie" rule is enforced mechanically rather than by discipline: every message passes through a redaction step before it reaches the file, the buffer or the dashboard.

- [x] **Step 1: Write the failing tests**

`tests/GeoVali.Tests/RunLogTests.cs`:

```csharp
using GeoVali.Running;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class RunLogTests
{
    [Fact]
    public void Keeps_recent_entries_in_memory_for_the_dashboard()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        log.Info("Regenerating An Arbitrary Africa");
        log.Error("Publishing failed");

        var recent = log.Recent();
        Assert.Equal(2, recent.Count);
        Assert.Equal("info", recent[0].level);
        Assert.Equal("Regenerating An Arbitrary Africa", recent[0].message);
        Assert.Equal("error", recent[1].level);
    }

    [Fact]
    public void Caps_the_in_memory_buffer_and_keeps_the_newest()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        for (var i = 0; i < RunLog.BufferSize + 50; i++)
        {
            log.Info($"line {i}");
        }

        var recent = log.Recent();
        Assert.Equal(RunLog.BufferSize, recent.Count);
        Assert.Equal($"line {RunLog.BufferSize + 49}", recent[^1].message);
        Assert.Equal("line 50", recent[0].message);
    }

    [Fact]
    public void Writes_to_a_dated_file_in_the_log_directory()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        log.Info("Regenerating An Arbitrary Africa");

        var file = Directory.GetFiles(temp.Path, "geovali-*.log").Single();
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd"), file);
        Assert.Contains("Regenerating An Arbitrary Africa", File.ReadAllText(file));
    }

    [Fact]
    public void Never_lets_the_cookie_reach_the_file_the_buffer_or_a_subscriber()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => "ncfa-super-secret-value");
        var seen = new List<LogEntry>();
        log.Appended += seen.Add;

        // Something careless interpolated the cookie into a message.
        log.Error("Request failed with Cookie: _ncfa=ncfa-super-secret-value");

        var fileText = File.ReadAllText(Directory.GetFiles(temp.Path, "geovali-*.log").Single());
        Assert.DoesNotContain("ncfa-super-secret-value", fileText);
        Assert.DoesNotContain("ncfa-super-secret-value", log.Recent().Single().message);
        Assert.DoesNotContain("ncfa-super-secret-value", seen.Single().message);
        Assert.Contains("***", log.Recent().Single().message);
    }

    [Fact]
    public void Raises_appended_for_live_dashboard_streaming()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);
        var seen = new List<LogEntry>();
        log.Appended += seen.Add;

        log.Info("Finding locations in Botswana");

        Assert.Equal("Finding locations in Botswana", seen.Single().message);
    }

    [Fact]
    public void Prunes_log_files_older_than_the_retention_window()
    {
        using var temp = new TempDir();
        var old = Path.Combine(temp.Path, "geovali-2020-01-01.log");
        var recent = Path.Combine(temp.Path, $"geovali-{DateTime.UtcNow:yyyy-MM-dd}.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-(RunLog.RetentionDays + 1)));

        new RunLog(temp.Path, () => null).PruneOldFiles();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Survives_a_log_directory_it_cannot_write_to()
    {
        // Logging is a side channel; a failure to write must never take down a run.
        var unwritable = Path.Combine(Path.GetTempPath(), "geovali-tests", Guid.NewGuid().ToString("N"), "\0bad");
        var log = new RunLog(unwritable, () => null);

        log.Info("still buffered");

        Assert.Equal("still buffered", log.Recent().Single().message);
    }
}
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RunLogTests`
Expected: FAIL — `RunLog` does not exist.

- [x] **Step 3: Write `RunLog`**

`src/GeoVali/Running/RunLog.cs`:

```csharp
using System.Globalization;

namespace GeoVali.Running;

public sealed record LogEntry(DateTime timestampUtc, string level, string message);

/// <summary>
/// A rolling log file in the config directory holds detail; the dashboard shows the recent
/// in-memory buffer. Neither ever contains the cookie: every message is redacted on the way in,
/// so the rule holds no matter who writes the message.
/// </summary>
public sealed class RunLog(string logDirectory, Func<string?> cookieProvider)
{
    public const int BufferSize = 500;
    public const int RetentionDays = 14;

    private readonly Queue<LogEntry> _buffer = new(BufferSize);
    private readonly Lock _gate = new();

    /// <summary>Raised for every entry, so the SSE endpoint can push it to the dashboard.</summary>
    public event Action<LogEntry>? Appended;

    public void Info(string message) => Append("info", message);

    public void Error(string message) => Append("error", message);

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    public void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(logDirectory, "geovali-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Pruning is housekeeping. Never let it matter.
        }
    }

    private void Append(string level, string message)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, Redact(message));

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > BufferSize)
            {
                _buffer.Dequeue();
            }
        }

        WriteToFile(entry);
        Appended?.Invoke(entry);
    }

    private string Redact(string message)
    {
        var cookie = cookieProvider();
        return string.IsNullOrEmpty(cookie) || cookie.Length < 8
            ? message
            : message.Replace(cookie, "***", StringComparison.Ordinal);
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, $"geovali-{entry.timestampUtc:yyyy-MM-dd}.log");
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{entry.timestampUtc:yyyy-MM-ddTHH:mm:ssZ} [{entry.level}] {entry.message}{Environment.NewLine}");
            File.AppendAllText(path, line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Logging is a side channel; a failure to write must never take down a run.
        }
    }
}
```

- [x] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RunLogTests`
Expected: PASS, 7 tests.

If `Lock` is unavailable, use `private readonly object _gate = new();` — behaviour is identical.

- [x] **Step 5: Commit**

```bash
git add src/GeoVali/Running/RunLog.cs tests/GeoVali.Tests/RunLogTests.cs
git commit -m "feat: run log with rolling file, ring buffer and cookie redaction"
```

---

### Task 11: UpdateRunner — preflight, per-map orchestration, stamping

**Files:**
- Create: `src/GeoVali/Running/RunModels.cs`
- Create: `src/GeoVali/Running/UpdateRunner.cs`
- Test: `tests/GeoVali.Tests/Support/FakeValiRunner.cs`
- Test: `tests/GeoVali.Tests/Support/FakeGeoguessrClient.cs`
- Test: `tests/GeoVali.Tests/UpdateRunnerTests.cs`

**Interfaces:**
- Consumes: `MapScanner`, `MapPaths`, `MapMetadataStore`, `GeoguessrMetadata`, `EphemeralMetadata`, `MapAvatar`, `Cadence`, `LocationFile`, `MapPublishGuard`, `NotPublishableException`, `DescriptionTemplate`, `IValiRunner`, `ValiNotFoundException`, `IGeoguessrClient`, `PublishRequest`, `GeoguessrAuthException`, `AvatarGenerator`, `RunLog`.
- Produces:
  - `GeoVali.Running.RunScope` — `enum { DueOnly, All, Single }`.
  - `GeoVali.Running.RunRequest` — `sealed record RunRequest(RunScope Scope, string? SingleMapDirectory = null)`.
  - `GeoVali.Running.MapRunOutcome` — `sealed record MapRunOutcome(string directory, string name, bool succeeded, bool skipped, string? error)`.
  - `GeoVali.Running.RunResult` — `sealed record RunResult(bool preflightFailed, string? preflightError, bool authInvalid, IReadOnlyList<MapRunOutcome> maps)`.
  - `GeoVali.Running.UpdateRunner` — `public UpdateRunner(IValiRunner vali, IGeoguessrClient geoguessr, RunLog log, Func<string?> mapsRootProvider, Func<int> defaultCadenceProvider, Func<DateTime> nowLocalProvider)` and `Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)`.

The ordering that matters most: auth is checked **before** any generation. Generation across a folder takes tens of minutes and a stale cookie is the most likely failure, so discovering it after regenerating everything must be structurally impossible.

- [x] **Step 1: Write the two fakes**

`tests/GeoVali.Tests/Support/FakeValiRunner.cs`:

```csharp
using GeoVali.Maps;
using GeoVali.Vali;

namespace GeoVali.Tests.Support;

/// <summary>An in-memory stand-in for vali. No process, no data download.</summary>
public sealed class FakeValiRunner : IValiRunner
{
    /// <summary>Set to null to simulate vali not being installed.</summary>
    public string? Executable { get; set; } = "/fake/vali";

    /// <summary>Map directory to the JSON that "vali" should write there. Missing means an empty array.</summary>
    public Dictionary<string, string> LocationsByDirectory { get; } = new(StringComparer.Ordinal);

    /// <summary>Map directory to the exit code to return. Missing means 0.</summary>
    public Dictionary<string, int> ExitCodeByDirectory { get; } = new(StringComparer.Ordinal);

    public List<string> GeneratedDirectories { get; } = [];

    public string? FindExecutable() => Executable;

    public Task<ValiResult> GenerateAsync(string mapDirectory, Action<string> onOutput, CancellationToken ct)
    {
        if (Executable is null)
        {
            throw new ValiNotFoundException($"vali was not found on your PATH. Install it with:  {ValiRunner.InstallCommand}");
        }

        GeneratedDirectories.Add(mapDirectory);
        onOutput($"Generating {Path.GetFileName(mapDirectory)}");

        var exitCode = ExitCodeByDirectory.GetValueOrDefault(mapDirectory, 0);
        if (exitCode == 0)
        {
            File.WriteAllText(
                MapPaths.Locations(mapDirectory),
                LocationsByDirectory.GetValueOrDefault(mapDirectory, "[]"));
        }

        return Task.FromResult(new ValiResult(exitCode, exitCode == 0 ? "Done." : "Country code XX is not valid."));
    }
}
```

`tests/GeoVali.Tests/Support/FakeGeoguessrClient.cs`:

```csharp
using GeoVali.Geoguessr;

namespace GeoVali.Tests.Support;

/// <summary>An in-memory stand-in for the GeoGuessr API. No sockets.</summary>
public sealed class FakeGeoguessrClient : IGeoguessrClient
{
    /// <summary>Null makes the auth probe report an expired cookie.</summary>
    public string? Nick { get; set; } = "slashP";

    /// <summary>Map id to the draft the API should report.</summary>
    public Dictionary<string, DraftInfo> Drafts { get; } = new(StringComparer.Ordinal);

    /// <summary>Map name to the exception PublishAsync should throw for it.</summary>
    public Dictionary<string, Exception> FailuresByMapName { get; } = new(StringComparer.Ordinal);

    /// <summary>Id handed back when a request arrives with no MapId.</summary>
    public string NewMapId { get; set; } = "newly-created-map-id";

    public List<PublishRequest> Published { get; } = [];

    public int AuthProbeCount { get; private set; }

    public Task<string?> GetSignedInUserNickAsync(CancellationToken ct)
    {
        AuthProbeCount++;
        return Task.FromResult(Nick);
    }

    public Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct) =>
        Task.FromResult(Drafts.GetValueOrDefault(mapId));

    public Task<string> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        if (FailuresByMapName.TryGetValue(request.Name, out var failure))
        {
            throw failure;
        }

        Published.Add(request);
        return Task.FromResult(request.MapId ?? NewMapId);
    }
}
```

- [x] **Step 2: Write the failing tests**

`tests/GeoVali.Tests/UpdateRunnerTests.cs`:

```csharp
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class UpdateRunnerTests
{
    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},
         {"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},
         {"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private const string FourLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},
         {"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},
         {"lat":7,"lng":8,"heading":40,"panoId":"d"}]
        """;

    private static readonly DateTime Now = new(2026, 9, 6, 3, 0, 0, DateTimeKind.Local);

    private sealed class Fixture : IDisposable
    {
        public TempDir Temp { get; } = new();
        public FakeValiRunner Vali { get; } = new();
        public FakeGeoguessrClient Geoguessr { get; } = new();
        public RunLog Log { get; }
        public int DefaultCadenceDays { get; set; } = 7;

        public Fixture() => Log = new RunLog(Path.Combine(Temp.Path, "logs"), () => null);

        public UpdateRunner Runner() => new(
            Vali, Geoguessr, Log,
            mapsRootProvider: () => Path.Combine(Temp.Path, "maps"),
            defaultCadenceProvider: () => DefaultCadenceDays,
            nowLocalProvider: () => Now);

        /// <summary>Creates a configured map folder and returns its directory.</summary>
        public async Task<string> AddMap(
            string folderName,
            GeoguessrMetadata metadata,
            EphemeralMetadata? ephemeral = null,
            string locations = FiveLocations)
        {
            var dir = Temp.Dir("maps", folderName);
            await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
            await MapMetadataStore.WriteMetadataAsync(dir, metadata);
            if (ephemeral is not null)
            {
                await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
            }

            Vali.LocationsByDirectory[dir] = locations;
            return dir;
        }

        /// <summary>Creates a folder with map.json but no geoguessr.json.</summary>
        public async Task<string> AddUnconfiguredMap(string folderName)
        {
            var dir = Temp.Dir("maps", folderName);
            await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
            return dir;
        }

        public void Dispose() => Temp.Dispose();
    }

    private static GeoguessrMetadata Metadata(string name, string? id = "existing-id", int? cadence = null, bool published = true) =>
        new()
        {
            id = id,
            name = name,
            description = "{{LocationCount}} locations.",
            avatar = new MapAvatar { background = "evening", landscape = "skyline", ground = "yellow", decoration = "japanese" },
            published = published,
            updateFrequencyDays = cadence
        };

    // ---- Preflight ----

    [Fact]
    public async Task Aborts_before_touching_any_map_when_vali_is_missing()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));
        fixture.Vali.Executable = null;

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.Contains("dotnet tool install -g vali", result.preflightError);
        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    [Fact]
    public async Task Checks_auth_before_generating_anything()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));
        fixture.Geoguessr.Nick = null; // expired cookie

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.True(result.authInvalid);
        // The whole point of the ordering: nothing was regenerated.
        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    [Fact]
    public async Task Probes_auth_exactly_once_per_run_not_once_per_map()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("Africa"));
        await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal(1, fixture.Geoguessr.AuthProbeCount);
    }

    // ---- Cadence and scope ----

    [Fact]
    public async Task Due_only_skips_a_map_published_today()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 5 });

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.True(result.maps.Single(m => m.directory == dir).skipped);
    }

    [Fact]
    public async Task Due_only_runs_a_map_whose_cadence_has_elapsed()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-11).ToUniversalTime(), updateCount = 5 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Equal([dir], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_everything_ignores_cadence()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: 10),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 5 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal([dir], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Single_scope_runs_only_the_named_map_and_ignores_its_cadence()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.ToUniversalTime(), updateCount = 1 });
        await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.Single, africa), CancellationToken.None);

        Assert.Equal([africa], fixture.Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Falls_back_to_the_global_default_cadence()
    {
        using var fixture = new Fixture();
        fixture.DefaultCadenceDays = 7;
        var dir = await fixture.AddMap("africa", Metadata("Africa", cadence: null),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-6).ToUniversalTime(), updateCount = 1 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.DueOnly), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories); // 6 days < 7
    }

    [Fact]
    public async Task Ignores_a_folder_that_has_no_geoguessr_json()
    {
        using var fixture = new Fixture();
        await fixture.AddUnconfiguredMap("brand-new");

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Empty(fixture.Vali.GeneratedDirectories);
        Assert.Empty(result.maps);
    }

    // ---- The happy path ----

    [Fact]
    public async Task Publishes_with_the_expanded_description_and_the_generated_locations()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var request = fixture.Geoguessr.Published.Single();
        Assert.Equal("An Arbitrary Africa", request.Name);
        Assert.Equal("5 locations.", request.Description);
        Assert.Equal(5, request.Locations.Count);
        Assert.True(request.Publish);
        Assert.Equal("existing-id", request.MapId);
    }

    [Fact]
    public async Task Leaves_the_token_unsubstituted_in_geoguessr_json()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("An Arbitrary Africa"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("{{LocationCount}} locations.", metadata!.description);
    }

    [Fact]
    public async Task Stamps_the_ephemeral_file_on_success()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("africa", Metadata("Africa"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 306 });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(307, ephemeral.updateCount);
        Assert.True(ephemeral.lastPublishedTimeUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.NotNull(ephemeral.lastRunUtc);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task Writes_the_new_id_back_when_a_map_is_created()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("brand-new", Metadata("Brand New", id: null));
        fixture.Geoguessr.NewMapId = "6a9d6c505a43d0a64f98be5c";

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("6a9d6c505a43d0a64f98be5c", metadata!.id);
    }

    [Fact]
    public async Task Generates_and_persists_an_avatar_when_the_map_has_none()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("no-avatar", Metadata("No Avatar") with { avatar = null });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.NotNull(metadata!.avatar);
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar!.background));
        Assert.Equal(metadata.avatar.background, fixture.Geoguessr.Published.Single().Avatar.background);
    }

    [Fact]
    public async Task An_unpublished_map_is_sent_with_publish_false()
    {
        using var fixture = new Fixture();
        await fixture.AddMap("draft", Metadata("Draft Only", published: false));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(fixture.Geoguessr.Published.Single().Publish);
    }

    [Fact]
    public async Task Runs_maps_one_at_a_time_in_a_stable_order()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal([africa, europe], fixture.Vali.GeneratedDirectories);
    }

    // ---- Failure isolation ----

    [Fact]
    public async Task A_vali_failure_fails_that_map_and_moves_to_the_next()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Vali.ExitCodeByDirectory[africa] = 1;

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var failed = result.maps.Single(m => m.directory == africa);
        Assert.False(failed.succeeded);
        Assert.Contains("exit code 1", failed.error);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);
        Assert.Equal("Europe", fixture.Geoguessr.Published.Single().Name);
    }

    [Fact]
    public async Task Four_locations_fail_the_map_before_any_http_call()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("thin", Metadata("Thin Map"), locations: FourLocations);

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single().succeeded);
        Assert.Contains("at least 5", result.maps.Single().error);
        Assert.Empty(fixture.Geoguessr.Published);
    }

    [Fact]
    public async Task A_failed_map_is_not_stamped_so_it_stays_due()
    {
        using var fixture = new Fixture();
        var before = new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 306 };
        var dir = await fixture.AddMap("thin", Metadata("Thin Map"), before, locations: FourLocations);

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var after = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(before.lastPublishedTimeUtc, after.lastPublishedTimeUtc);
        Assert.Equal(306, after.updateCount);
        Assert.Contains("at least 5", after.lastError);
        Assert.NotNull(after.lastRunUtc);
    }

    [Fact]
    public async Task Clears_a_previous_error_after_a_successful_run()
    {
        using var fixture = new Fixture();
        var dir = await fixture.AddMap("recovered", Metadata("Recovered"),
            new EphemeralMetadata { lastPublishedTimeUtc = Now.AddDays(-30).ToUniversalTime(), updateCount = 1, lastError = "old failure" });

        await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Null((await MapMetadataStore.ReadEphemeralAsync(dir)).lastError);
    }

    [Fact]
    public async Task A_401_mid_run_aborts_the_whole_run_rather_than_failing_forty_maps()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Geoguessr.FailuresByMapName["Africa"] = new GeoguessrAuthException();

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.authInvalid);
        // Europe was never attempted, and the expired cookie is not recorded as its failure.
        Assert.DoesNotContain(result.maps, m => m.directory == europe);
        Assert.Equal([africa], fixture.Vali.GeneratedDirectories);
        Assert.Null((await MapMetadataStore.ReadEphemeralAsync(europe)).lastError);
    }

    [Fact]
    public async Task A_geoguessr_rejection_fails_only_that_map()
    {
        using var fixture = new Fixture();
        var africa = await fixture.AddMap("africa", Metadata("Africa"));
        var europe = await fixture.AddMap("europe", Metadata("Europe", id: "europe-id"));
        fixture.Geoguessr.FailuresByMapName["Africa"] = new GeoguessrException("GeoGuessr rejected updating the map: 500 Server Error.");

        var result = await fixture.Runner().RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single(m => m.directory == africa).succeeded);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);
        Assert.False(result.authInvalid);
    }

    [Fact]
    public async Task Reports_when_the_configured_maps_root_is_not_set()
    {
        using var fixture = new Fixture();
        var runner = new UpdateRunner(fixture.Vali, fixture.Geoguessr, fixture.Log,
            mapsRootProvider: () => null,
            defaultCadenceProvider: () => 7,
            nowLocalProvider: () => Now);

        var result = await runner.RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.Contains("maps folder", result.preflightError);
    }
}
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~UpdateRunnerTests`
Expected: FAIL — `UpdateRunner` does not exist.

- [x] **Step 4: Write the run models**

`src/GeoVali/Running/RunModels.cs`:

```csharp
namespace GeoVali.Running;

public enum RunScope
{
    /// <summary>Only maps whose cadence has elapsed. What the scheduler asks for.</summary>
    DueOnly,

    /// <summary>Every configured map, cadence ignored. "Run everything" in the dashboard.</summary>
    All,

    /// <summary>One map, cadence ignored. The per-row "Run now".</summary>
    Single
}

public sealed record RunRequest(RunScope Scope, string? SingleMapDirectory = null);

public sealed record MapRunOutcome(string directory, string name, bool succeeded, bool skipped, string? error);

public sealed record RunResult(
    bool preflightFailed,
    string? preflightError,
    bool authInvalid,
    IReadOnlyList<MapRunOutcome> maps);
```

- [x] **Step 5: Write `UpdateRunner`**

`src/GeoVali/Running/UpdateRunner.cs`:

```csharp
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
    Func<DateTime> nowLocalProvider)
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
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~UpdateRunnerTests`
Expected: PASS, 22 tests.

- [x] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything green.

- [x] **Step 8: Commit**

```bash
git add src/GeoVali/Running tests/GeoVali.Tests/UpdateRunnerTests.cs \
        tests/GeoVali.Tests/Support/FakeValiRunner.cs tests/GeoVali.Tests/Support/FakeGeoguessrClient.cs
git commit -m "feat: run loop with preflight, per-map isolation and stamp-only-on-success"
```

---

### Task 12: Scheduler and the single run lock

**Files:**
- Create: `src/GeoVali/Running/RunCoordinator.cs`
- Create: `src/GeoVali/Running/Scheduler.cs`
- Test: `tests/GeoVali.Tests/RunCoordinatorTests.cs`

**Interfaces:**
- Consumes: `UpdateRunner`, `RunRequest`, `RunResult`, `RunScope`, `RunLog` (Task 11), `ConfigStore` (Task 9).
- Produces:
  - `GeoVali.Running.RunCoordinator` — `public RunCoordinator(UpdateRunner runner, RunLog log)`,
    `Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)`,
    `bool IsRunning { get; }`, `string? CurrentMapName { get; set; }`,
    `DateTime? NextScheduledRunUtc { get; set; }`, `RunResult? LastResult { get; }`.
  - `GeoVali.Running.Scheduler : BackgroundService` — `public Scheduler(RunCoordinator coordinator, ConfigStore config, RunLog log)`.

A single run-lock means a manual "Run now" during an active run **joins** the in-flight run rather than starting a second. That is the whole rule: never two vali processes at once.

- [x] **Step 1: Make the fake vali runner overridable**

`RunCoordinator` needs a vali stand-in that can block mid-run, so the fake from Task 11 must be
subclassable. Change two words in `tests/GeoVali.Tests/Support/FakeValiRunner.cs`:

```csharp
public class FakeValiRunner : IValiRunner                       // was: public sealed class
{
    // ...unchanged fields...

    public virtual Task<ValiResult> GenerateAsync(              // was: public Task<ValiResult>
        string mapDirectory, Action<string> onOutput, CancellationToken ct)
    {
        // ...unchanged body...
    }
}
```

- [x] **Step 2: Write the failing tests**

`tests/GeoVali.Tests/RunCoordinatorTests.cs`:

```csharp
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
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RunCoordinatorTests`
Expected: FAIL — `The type or namespace name 'RunCoordinator' could not be found`.

- [x] **Step 4: Write `RunCoordinator`**

`src/GeoVali/Running/RunCoordinator.cs`:

```csharp
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
```

- [x] **Step 5: Write `Scheduler`**

`src/GeoVali/Running/Scheduler.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using GeoVali.Configuration;

namespace GeoVali.Running;

/// <summary>
/// Wakes on the configured interval and asks for a due-only run. It never forces a run: the
/// cadence decides which maps are actually touched.
/// </summary>
public sealed class Scheduler(RunCoordinator coordinator, ConfigStore config, RunLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.PruneOldFiles();

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, config.Current.checkIntervalMinutes));
            coordinator.NextScheduledRunUtc = DateTime.UtcNow.Add(interval);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.Current.mapsRoot))
            {
                continue; // Still in first-run setup.
            }

            try
            {
                await coordinator.RunAsync(new RunRequest(RunScope.DueOnly), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RunCoordinatorTests`
Expected: PASS, 5 tests.

- [x] **Step 7: Commit**

```bash
git add src/GeoVali/Running/RunCoordinator.cs src/GeoVali/Running/Scheduler.cs \
        tests/GeoVali.Tests/RunCoordinatorTests.cs tests/GeoVali.Tests/Support/FakeValiRunner.cs
git commit -m "feat: background scheduler with a single run lock"
```

---

### Task 13: End-to-end run with a fake vali executable and a stub GeoGuessr server

**Files:**
- Create: `tests/GeoVali.Tests/Support/StubGeoguessrServer.cs`
- Test: `tests/GeoVali.Tests/EndToEndRunTests.cs`
- Modify: `tests/GeoVali.Tests/GeoVali.Tests.csproj` (framework reference for the stub server)

**Interfaces:**
- Consumes: everything from Tasks 2–12.
- Produces: `GeoVali.Tests.Support.StubGeoguessrServer` — `IAsyncDisposable`, `static Task<StubGeoguessrServer> StartAsync()`, properties `string BaseAddress`, `List<(string Method, string Path, string Body)> Calls`, `bool Unauthorized { get; set; }`, `Dictionary<string,int> DraftVersions { get; }`, `HashSet<string> RejectPublishForMapIds { get; }`.

This is the one test that runs the real `ValiRunner` against a real child process and the real `GeoguessrClient` against a real socket. Everything between them is production code.

- [x] **Step 1: Let the test project host a web server**

The stub server builds a real `WebApplication`, so the test project needs the ASP.NET Core shared
framework. Add to `tests/GeoVali.Tests/GeoVali.Tests.csproj`:

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [x] **Step 2: Write the stub server**

`tests/GeoVali.Tests/Support/StubGeoguessrServer.cs`:

```csharp
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoVali.Tests.Support;

/// <summary>
/// A real HTTP server on a loopback port that answers the four GeoGuessr calls GeoVali makes.
/// Lets the end-to-end test exercise the production HttpClient stack rather than a handler stub.
/// </summary>
public sealed class StubGeoguessrServer : IAsyncDisposable
{
    private WebApplication _app = null!;

    public string BaseAddress { get; private set; } = "";

    public List<(string Method, string Path, string Body)> Calls { get; } = [];

    /// <summary>When true every call answers 401, simulating an expired cookie.</summary>
    public bool Unauthorized { get; set; }

    /// <summary>Map id to the version the GET should report. Missing means 0.</summary>
    public Dictionary<string, int> DraftVersions { get; } = new(StringComparer.Ordinal);

    /// <summary>Map ids whose PUT should fail with 500, to exercise per-map failure isolation.</summary>
    public HashSet<string> RejectUpdateForMapIds { get; } = new(StringComparer.Ordinal);

    public static async Task<StubGeoguessrServer> StartAsync()
    {
        var server = new StubGeoguessrServer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            lock (server.Calls)
            {
                server.Calls.Add((context.Request.Method, context.Request.Path.Value ?? "", body));
            }

            if (server.Unauthorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.MapGet("/api/v3/profiles", () => Results.Json(new { user = new { nick = "slashP", id = "u1" } }));

        app.MapPost("/api/v4/user-maps/drafts", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var name = JsonNode.Parse(await reader.ReadToEndAsync())?["name"]?.GetValue<string>() ?? "";
            var id = $"created-{name.Replace(" ", "-").ToLowerInvariant()}";
            return Results.Json(new { id, name });
        });

        app.MapGet("/api/v4/user-maps/drafts/{id}", (string id) =>
            Results.Json(new { id, name = id, version = server.DraftVersions.GetValueOrDefault(id, 0) }));

        app.MapPut("/api/v4/user-maps/drafts/{id}", (string id) =>
            server.RejectUpdateForMapIds.Contains(id)
                ? Results.Json(new { message = "Something went wrong" }, statusCode: StatusCodes.Status500InternalServerError)
                : Results.Json(new { id }));

        app.MapPut("/api/v4/user-maps/drafts/{id}/publish", (string id) => Results.Json(new { id }));

        await app.StartAsync();
        server._app = app;
        server.BaseAddress = app.Urls.First().TrimEnd('/') + "/";
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
```

- [x] **Step 3: Write the failing end-to-end tests**

`tests/GeoVali.Tests/EndToEndRunTests.cs`:

```csharp
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using GeoVali.Vali;
using Xunit;

namespace GeoVali.Tests;

public class EndToEndRunTests
{
    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private const string FourLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"}]
        """;

    private static GeoguessrMetadata Metadata(string name, string? id) => new()
    {
        id = id,
        name = name,
        description = "{{LocationCount}} locations.",
        avatar = new MapAvatar { background = "day", landscape = "hills", ground = "green", decoration = "none" },
        published = true
    };

    private static async Task<string> AddMap(TempDir temp, string folderName, GeoguessrMetadata metadata)
    {
        var dir = temp.Dir("maps", folderName);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        await MapMetadataStore.WriteMetadataAsync(dir, metadata);
        return dir;
    }

    private static UpdateRunner BuildRunner(TempDir temp, StubGeoguessrServer server, string fakeVali)
    {
        var http = new HttpClient(new TransientRetryHandler((_, _) => Task.CompletedTask)
        {
            InnerHandler = new HttpClientHandler()
        })
        {
            BaseAddress = new Uri(server.BaseAddress)
        };
        http.DefaultRequestHeaders.Add("Cookie", "_ncfa=test-cookie");

        var log = new RunLog(Path.Combine(temp.Path, "logs"), () => "test-cookie");
        return new UpdateRunner(
            new ValiRunner(fakeVali),
            new GeoguessrClient(http),
            log,
            () => Path.Combine(temp.Path, "maps"),
            () => 7,
            () => DateTime.Now);
    }

    [Fact]
    public async Task Generates_publishes_and_stamps_a_map_end_to_end()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.DraftVersions["africa-id"] = 306;

        var dir = await AddMap(temp, "africa", Metadata("An Arbitrary Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.maps.Single().succeeded);

        // vali really ran and really wrote the locations file.
        Assert.True(File.Exists(MapPaths.Locations(dir)));

        // The four-call sequence really went over the wire, in order, with version + 1.
        var paths = server.Calls.Select(c => $"{c.Method} {c.Path}").ToArray();
        Assert.Equal([
            "GET /api/v3/profiles",
            "GET /api/v4/user-maps/drafts/africa-id",
            "PUT /api/v4/user-maps/drafts/africa-id",
            "PUT /api/v4/user-maps/drafts/africa-id/publish"
        ], paths);
        Assert.Contains("\"version\":307", server.Calls[2].Body);
        Assert.Contains("\"description\":\"5 locations.\"", server.Calls[2].Body);

        // Stamped.
        var ephemeral = await MapMetadataStore.ReadEphemeralAsync(dir);
        Assert.Equal(1, ephemeral.updateCount);
        Assert.Null(ephemeral.lastError);
    }

    [Fact]
    public async Task A_401_at_preflight_aborts_before_vali_runs()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.Unauthorized = true;

        var dir = await AddMap(temp, "africa", Metadata("An Arbitrary Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.True(result.preflightFailed);
        Assert.True(result.authInvalid);

        // The whole point: nothing was regenerated, so no time was wasted on a stale cookie.
        Assert.False(File.Exists(MapPaths.Locations(dir)));
        Assert.Single(server.Calls);
        Assert.Equal("/api/v3/profiles", server.Calls.Single().Path);
    }

    [Fact]
    public async Task One_bad_map_never_stops_the_others()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        server.RejectUpdateForMapIds.Add("africa-id");

        var africa = await AddMap(temp, "africa", Metadata("Africa", "africa-id"));
        var europe = await AddMap(temp, "europe", Metadata("Europe", "europe-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single(m => m.directory == africa).succeeded);
        Assert.True(result.maps.Single(m => m.directory == europe).succeeded);

        // Stamp only on success: africa stays due, europe does not.
        Assert.Equal(0, (await MapMetadataStore.ReadEphemeralAsync(africa)).updateCount);
        Assert.NotNull((await MapMetadataStore.ReadEphemeralAsync(africa)).lastError);
        Assert.Equal(1, (await MapMetadataStore.ReadEphemeralAsync(europe)).updateCount);
    }

    [Fact]
    public async Task Four_locations_fail_the_map_without_any_publish_call()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();

        var dir = await AddMap(temp, "thin", Metadata("Thin Map", "thin-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FourLocations);

        var result = await BuildRunner(temp, server, fakeVali)
            .RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.False(result.maps.Single().succeeded);
        Assert.Contains("at least 5", result.maps.Single().error);

        // Only the auth probe. No draft was read and nothing was written.
        Assert.Single(server.Calls);
        Assert.Equal(0, (await MapMetadataStore.ReadEphemeralAsync(dir)).updateCount);
    }

    [Fact]
    public async Task A_brand_new_map_is_created_and_its_id_written_back()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();

        var dir = await AddMap(temp, "fresh", Metadata("Fresh Map", id: null));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        await BuildRunner(temp, server, fakeVali).RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        Assert.Equal("created-fresh-map", (await MapMetadataStore.ReadMetadataAsync(dir))!.id);
        Assert.Contains(server.Calls, c => c is { Method: "POST", Path: "/api/v4/user-maps/drafts" });
    }

    [Fact]
    public async Task The_cookie_never_appears_in_the_log_file()
    {
        using var temp = new TempDir();
        await using var server = await StubGeoguessrServer.StartAsync();
        await AddMap(temp, "africa", Metadata("Africa", "africa-id"));
        var fakeVali = FakeValiExecutable.Create(temp, FiveLocations);

        await BuildRunner(temp, server, fakeVali).RunAsync(new RunRequest(RunScope.All), CancellationToken.None);

        var logs = Directory.GetFiles(Path.Combine(temp.Path, "logs"), "geovali-*.log");
        Assert.All(logs, file => Assert.DoesNotContain("test-cookie", File.ReadAllText(file)));
    }
}
```

- [x] **Step 4: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EndToEndRunTests`
Expected: FAIL — `StubGeoguessrServer` does not exist until Step 2 compiles; after that all six should pass. If any fail for a real reason, fix the production code rather than the test.

- [x] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add tests/GeoVali.Tests/EndToEndRunTests.cs tests/GeoVali.Tests/Support/StubGeoguessrServer.cs \
        tests/GeoVali.Tests/GeoVali.Tests.csproj
git commit -m "test: end-to-end run against a fake vali executable and a stub server"
```

---

### Task 14: Web host, service wiring, status and maps endpoints

**Files:**
- Modify: `src/GeoVali/Program.cs`
- Create: `src/GeoVali/Web/LocalOnlyMiddleware.cs`
- Create: `src/GeoVali/Web/ApiEndpoints.cs`
- Create: `src/GeoVali/Web/ApiModels.cs`
- Create: `src/GeoVali/Web/ServiceRegistration.cs`
- Create: `src/GeoVali/Geoguessr/CookieHandler.cs`
- Test: `tests/GeoVali.Tests/ApiEndpointTests.cs`
- Modify: `tests/GeoVali.Tests/GeoVali.Tests.csproj` (add `Microsoft.AspNetCore.Mvc.Testing`)

**Interfaces:**
- Consumes: everything from Tasks 2–12.
- Produces:
  - `GeoVali.Web.LocalOnlyMiddleware` — `public static IApplicationBuilder UseLocalOnly(this IApplicationBuilder app)`, `const string RequiredHeader = "X-GeoVali"`.
  - `GeoVali.Web.ApiEndpoints.MapGeoValiApi(this WebApplication app)` → `WebApplication`.
  - `GeoVali.Web.StatusResponse` — `sealed record StatusResponse(bool setupComplete, string? mapsRoot, bool valiInstalled, string valiInstallCommand, bool authValid, string? userNick, DateTime? nextRunUtc, bool running, string? currentMap, string version)`.
  - `GeoVali.Web.MapRow` — `sealed record MapRow(string directory, string folderName, bool configured, string name, bool published, int cadenceDays, DateTime? lastPublishedTimeUtc, int updateCount, string? lastError, bool due)`.
  - `GeoVali.Web.ServiceRegistration.AddGeoVali(this IServiceCollection services, string configDirectory)` → `IServiceCollection`.
  - `GeoVali.Geoguessr.CookieHandler : DelegatingHandler` — `public CookieHandler(CredentialStore credentials)`.

Two security decisions live here. The server binds to loopback only, and every mutating endpoint requires the `X-GeoVali: 1` header — a browser cannot send a custom header cross-origin without a preflight, and no CORS headers are emitted, so a malicious page the user happens to have open cannot drive the tool.

- [x] **Step 1: Add the integration-testing package**

```bash
dotnet add tests/GeoVali.Tests/GeoVali.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

- [x] **Step 2: Write the failing tests**

`tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Maps;
using GeoVali.Running;
using GeoVali.Tests.Support;
using GeoVali.Vali;
using Xunit;

namespace GeoVali.Tests;

public class ApiEndpointTests : IDisposable
{
    private readonly TempDir _temp = new();

    private const string FiveLocations = """
        [{"lat":1,"lng":2,"heading":10,"panoId":"a"},{"lat":3,"lng":4,"heading":20,"panoId":"b"},
         {"lat":5,"lng":6,"heading":30,"panoId":"c"},{"lat":7,"lng":8,"heading":40,"panoId":"d"},
         {"lat":9,"lng":10,"heading":50,"panoId":"e"}]
        """;

    private FakeValiRunner Vali { get; } = new();
    private FakeGeoguessrClient Geoguessr { get; } = new();

    private string MapsRoot => Path.Combine(_temp.Path, "maps");

    /// <summary>Hosts the real app with the two outside-world dependencies replaced.</summary>
    private HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValiRunner>();
                services.RemoveAll<IGeoguessrClient>();
                services.RemoveAll<IHostedService>();   // no background scheduler in tests
                services.AddSingleton<IValiRunner>(Vali);
                services.AddSingleton<IGeoguessrClient>(Geoguessr);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-GeoVali", "1");
        return client;
    }

    private async Task<string> AddConfiguredMap(string folderName, string name, int? cadence = null, EphemeralMetadata? ephemeral = null)
    {
        var dir = Path.Combine(MapsRoot, folderName);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        await MapMetadataStore.WriteMetadataAsync(dir, new GeoguessrMetadata
        {
            id = $"{folderName}-id",
            name = name,
            description = "{{LocationCount}} locations.",
            avatar = new MapAvatar { background = "day", landscape = "hills", ground = "green", decoration = "none" },
            published = true,
            updateFrequencyDays = cadence
        });
        if (ephemeral is not null)
        {
            await MapMetadataStore.WriteEphemeralAsync(dir, ephemeral);
        }

        Vali.LocationsByDirectory[dir] = FiveLocations;
        return dir;
    }

    private void ConfigureMapsRoot()
    {
        Directory.CreateDirectory(MapsRoot);
        new ConfigStore(_temp.Path).Write(new AppConfig { mapsRoot = MapsRoot });
        new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).WriteCookie("test-cookie");
    }

    [Fact]
    public async Task Status_reports_setup_incomplete_before_a_maps_folder_is_chosen()
    {
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["setupComplete"]!.GetValue<bool>());
        Assert.Null(status["mapsRoot"]?.GetValue<string>());
        Assert.Equal(ValiRunner.InstallCommand, status["valiInstallCommand"]!.GetValue<string>());
    }

    [Fact]
    public async Task Status_reports_the_signed_in_user_and_the_maps_folder_once_set_up()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.True(status!["setupComplete"]!.GetValue<bool>());
        Assert.Equal(MapsRoot, status["mapsRoot"]!.GetValue<string>());
        Assert.True(status["authValid"]!.GetValue<bool>());
        Assert.Equal("slashP", status["userNick"]!.GetValue<string>());
        Assert.True(status["valiInstalled"]!.GetValue<bool>());
        Assert.False(status["running"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Status_reports_an_expired_cookie()
    {
        ConfigureMapsRoot();
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["authValid"]!.GetValue<bool>());
        Assert.Null(status["userNick"]?.GetValue<string>());
    }

    [Fact]
    public async Task Status_reports_that_vali_is_missing()
    {
        ConfigureMapsRoot();
        Vali.Executable = null;
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonNode>("/api/status");

        Assert.False(status!["valiInstalled"]!.GetValue<bool>());
        Assert.Equal("dotnet tool install -g vali", status["valiInstallCommand"]!.GetValue<string>());
    }

    [Fact]
    public async Task Maps_lists_configured_maps_with_their_state()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "An Arbitrary Africa", cadence: 10, ephemeral: new EphemeralMetadata
        {
            lastPublishedTimeUtc = DateTime.UtcNow.AddDays(-20),
            updateCount = 307,
            lastError = "vali exited with code 1."
        });
        using var client = CreateClient();

        var maps = await client.GetFromJsonAsync<JsonNode>("/api/maps");

        var row = maps!.AsArray().Single();
        Assert.Equal("An Arbitrary Africa", row["name"]!.GetValue<string>());
        Assert.True(row["configured"]!.GetValue<bool>());
        Assert.Equal(10, row["cadenceDays"]!.GetValue<int>());
        Assert.Equal(307, row["updateCount"]!.GetValue<int>());
        Assert.Equal("vali exited with code 1.", row["lastError"]!.GetValue<string>());
        Assert.True(row["due"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Maps_lists_a_folder_without_geoguessr_json_as_not_set_up()
    {
        ConfigureMapsRoot();
        var dir = Path.Combine(MapsRoot, "brand-new");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        using var client = CreateClient();

        var row = (await client.GetFromJsonAsync<JsonNode>("/api/maps"))!.AsArray().Single();

        Assert.False(row["configured"]!.GetValue<bool>());
        Assert.Equal("brand-new", row["folderName"]!.GetValue<string>());
        Assert.Equal("brand-new", row["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Maps_uses_the_global_default_cadence_when_the_map_sets_none()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa", cadence: null);
        using var client = CreateClient();

        var row = (await client.GetFromJsonAsync<JsonNode>("/api/maps"))!.AsArray().Single();

        Assert.Equal(7, row["cadenceDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task Maps_returns_an_empty_list_before_setup()
    {
        using var client = CreateClient();

        var maps = await client.GetFromJsonAsync<JsonNode>("/api/maps");

        Assert.Empty(maps!.AsArray());
    }

    [Fact]
    public async Task A_mutating_call_without_the_geovali_header_is_refused()
    {
        ConfigureMapsRoot();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValiRunner>();
                services.RemoveAll<IGeoguessrClient>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IValiRunner>(Vali);
                services.AddSingleton<IGeoguessrClient>(Geoguessr);
            });
        });
        using var client = factory.CreateClient(); // no X-GeoVali header

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "all" });

        // A page on another origin cannot set a custom header without a preflight we never answer.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_read_only_call_without_the_header_still_works()
    {
        using var client = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
                builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
            })
            .CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
    }

    public void Dispose() => _temp.Dispose();
}
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: FAIL — the endpoints do not exist.

- [x] **Step 4: Write the API models**

`src/GeoVali/Web/ApiModels.cs`:

```csharp
namespace GeoVali.Web;

public sealed record StatusResponse(
    bool setupComplete,
    string? mapsRoot,
    bool valiInstalled,
    string valiInstallCommand,
    bool authValid,
    string? userNick,
    DateTime? nextRunUtc,
    bool running,
    string? currentMap,
    string version);

public sealed record MapRow(
    string directory,
    string folderName,
    bool configured,
    string name,
    bool published,
    int cadenceDays,
    DateTime? lastPublishedTimeUtc,
    int updateCount,
    string? lastError,
    bool due);
```

- [x] **Step 5: Write the local-only middleware**

`src/GeoVali/Web/LocalOnlyMiddleware.cs`:

```csharp
using System.Net;

namespace GeoVali.Web;

/// <summary>
/// The dashboard is a local tool. Two guards: the request must come from loopback, and any
/// mutating call must carry a custom header. A page on another origin cannot set a custom header
/// without a CORS preflight, and GeoVali answers none, so it cannot drive the tool.
/// </summary>
public static class LocalOnlyMiddleware
{
    public const string RequiredHeader = "X-GeoVali";

    public static IApplicationBuilder UseLocalOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "GeoVali only accepts local connections." });
                return;
            }

            var isMutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            if (isMutating && !context.Request.Headers.ContainsKey(RequiredHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = $"Missing {RequiredHeader} header." });
                return;
            }

            await next();
        });
}
```

- [x] **Step 6: Write the status and maps endpoints**

`src/GeoVali/Web/ApiEndpoints.cs`:

```csharp
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

        return app;
    }
}
```

- [x] **Step 7: Wire the host**

`src/GeoVali/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using GeoVali;
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Running;
using GeoVali.Vali;
using GeoVali.Web;

var builder = WebApplication.CreateBuilder(args);

// Tests point this at a temp folder; normally it is the per-OS application data location.
var configDirectory = builder.Configuration["GeoVali:ConfigDirectory"] ?? AppPaths.ConfigDirectory;
Directory.CreateDirectory(configDirectory);

builder.Services.AddGeoVali(configDirectory);

var app = builder.Build();

app.UseLocalOnly();
app.MapGeoValiApi();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
```

`src/GeoVali/Web/ServiceRegistration.cs`:

```csharp
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Running;
using GeoVali.Vali;

namespace GeoVali.Web;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeoVali(this IServiceCollection services, string configDirectory)
    {
        services.AddSingleton(new ConfigStore(configDirectory));
        services.AddSingleton(_ => new CredentialStore(configDirectory, CredentialProtectorFactory.Create()));

        services.AddSingleton(provider => new RunLog(
            Path.Combine(configDirectory, "logs"),
            () => provider.GetRequiredService<CredentialStore>().ReadCookie()));

        services.AddSingleton<IValiRunner>(provider =>
            new ValiRunner(provider.GetRequiredService<ConfigStore>().Current.valiExecutablePath));

        // A DelegatingHandler attaches the cookie per request, reading the store each time, so a
        // freshly pasted cookie takes effect without restarting the tool and without any singleton
        // holding a stale header.
        services.AddTransient<TransientRetryHandler>(_ => new TransientRetryHandler());
        services.AddTransient<CookieHandler>();
        services.AddHttpClient<IGeoguessrClient, GeoguessrClient>(http =>
            {
                http.BaseAddress = new Uri(GeoguessrClient.BaseAddress);
                http.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddHttpMessageHandler<CookieHandler>()
            .AddHttpMessageHandler<TransientRetryHandler>();

        services.AddSingleton(provider => new UpdateRunner(
            provider.GetRequiredService<IValiRunner>(),
            provider.GetRequiredService<IGeoguessrClient>(),
            provider.GetRequiredService<RunLog>(),
            () => provider.GetRequiredService<ConfigStore>().Current.mapsRoot,
            () => provider.GetRequiredService<ConfigStore>().Current.defaultCadenceDays,
            () => DateTime.Now));

        services.AddSingleton(provider => new RunCoordinator(
            provider.GetRequiredService<UpdateRunner>(),
            provider.GetRequiredService<RunLog>()));

        services.AddHostedService<Scheduler>();

        return services;
    }
}
```

`src/GeoVali/Geoguessr/CookieHandler.cs`:

```csharp
using GeoVali.Configuration;

namespace GeoVali.Geoguessr;

/// <summary>
/// Attaches <c>Cookie: _ncfa=&lt;value&gt;</c> to every GeoGuessr request, read fresh from the
/// credential store each time. Keeping it here rather than on the HttpClient's default headers
/// means a newly pasted cookie takes effect immediately, and the cookie exists in exactly one
/// place in the request path.
/// </summary>
public sealed class CookieHandler(CredentialStore credentials) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookie = credentials.ReadCookie();
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", $"_ncfa={cookie}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```


- [x] **Step 8: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: PASS, 10 tests.

- [x] **Step 9: Commit**

```bash
git add src/GeoVali/Program.cs src/GeoVali/Web tests/GeoVali.Tests/ApiEndpointTests.cs \
        tests/GeoVali.Tests/GeoVali.Tests.csproj
git commit -m "feat: local-only web host with status and maps endpoints"
```

---

### Task 15: Run endpoints and live progress over Server-Sent Events

**Files:**
- Modify: `src/GeoVali/Web/ApiEndpoints.cs`
- Modify: `src/GeoVali/Running/UpdateRunner.cs` (report the current map to the coordinator)
- Modify: `tests/GeoVali.Tests/ApiEndpointTests.cs`

**Interfaces:**
- Consumes: `RunCoordinator`, `RunRequest`, `RunScope`, `RunLog`, `LogEntry` (Tasks 10–12).
- Produces:
  - `POST /api/run` accepting `{ "scope": "due" | "all" | "single", "directory": "..." }` → the `RunResult` as JSON.
  - `GET /api/events` → `text/event-stream`, replaying the recent buffer then streaming new entries.
  - `GeoVali.Web.RunRequestBody` — `sealed record RunRequestBody(string scope, string? directory)`.
  - `UpdateRunner` gains an optional `Action<string?>? onMapChanged` constructor parameter (last, defaulted to null), invoked with the map name before each map and null at the end.

- [x] **Step 1: Write the failing tests**

Append to `tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
    [Fact]
    public async Task Run_all_regenerates_and_publishes_every_configured_map()
    {
        ConfigureMapsRoot();
        var africa = await AddConfiguredMap("africa", "Africa");
        var europe = await AddConfiguredMap("europe", "Europe");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "all" });
        var result = await response.Content.ReadFromJsonAsync<JsonNode>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!["preflightFailed"]!.GetValue<bool>());
        Assert.Equal(2, result["maps"]!.AsArray().Count);
        Assert.Equal([africa, europe], Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_due_skips_a_map_published_today()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa", cadence: 10,
            ephemeral: new EphemeralMetadata { lastPublishedTimeUtc = DateTime.UtcNow, updateCount = 1 });
        using var client = CreateClient();

        await client.PostAsJsonAsync("/api/run", new { scope = "due" });

        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_single_regenerates_only_the_named_map()
    {
        ConfigureMapsRoot();
        var africa = await AddConfiguredMap("africa", "Africa");
        await AddConfiguredMap("europe", "Europe");
        using var client = CreateClient();

        await client.PostAsJsonAsync("/api/run", new { scope = "single", directory = africa });

        Assert.Equal([africa], Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_single_without_a_directory_is_a_bad_request()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run", new { scope = "single" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_rejects_a_directory_outside_the_maps_folder()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/run",
            new { scope = "single", directory = Path.Combine(_temp.Path, "somewhere-else") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Run_reports_a_preflight_abort_when_the_cookie_is_dead()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "Africa");
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var result = await (await client.PostAsJsonAsync("/api/run", new { scope = "all" }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(result!["preflightFailed"]!.GetValue<bool>());
        Assert.True(result["authInvalid"]!.GetValue<bool>());
        Assert.Empty(Vali.GeneratedDirectories);
    }

    [Fact]
    public async Task Events_streams_log_lines_from_a_run()
    {
        ConfigureMapsRoot();
        await AddConfiguredMap("africa", "An Arbitrary Africa");
        using var client = CreateClient();

        using var stream = await client.GetStreamAsync("/api/events");
        using var reader = new StreamReader(stream);

        await client.PostAsJsonAsync("/api/run", new { scope = "all" });

        var seen = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            seen.Add(line);
            if (line.Contains("An Arbitrary Africa"))
            {
                break;
            }
        }

        Assert.Contains(seen, line => line.StartsWith("data:") && line.Contains("An Arbitrary Africa"));
    }
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: FAIL — `/api/run` returns 404.

- [x] **Step 3: Let `UpdateRunner` report the current map**

In `src/GeoVali/Running/UpdateRunner.cs`, add a final defaulted constructor parameter:

```csharp
public sealed class UpdateRunner(
    IValiRunner vali,
    IGeoguessrClient geoguessr,
    RunLog log,
    Func<string?> mapsRootProvider,
    Func<int> defaultCadenceProvider,
    Func<DateTime> nowLocalProvider,
    Action<string?>? onMapChanged = null)
```

Wrap the existing `foreach (var map in candidates)` loop in a `try`/`finally` so the current map is
always cleared, however the loop exits, and announce each map at the top of the body:

```csharp
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

                // ...the unchanged try/catch around RunOneMap...
            }
        }
        finally
        {
            onMapChanged?.Invoke(null);
        }
```

- [x] **Step 4: Wire the callback in `ServiceRegistration`**

`RunCoordinator` is constructed after `UpdateRunner`, so pass a closure that resolves it lazily:

```csharp
        services.AddSingleton(provider => new UpdateRunner(
            provider.GetRequiredService<IValiRunner>(),
            provider.GetRequiredService<IGeoguessrClient>(),
            provider.GetRequiredService<RunLog>(),
            () => provider.GetRequiredService<ConfigStore>().Current.mapsRoot,
            () => provider.GetRequiredService<ConfigStore>().Current.defaultCadenceDays,
            () => DateTime.Now,
            name => provider.GetRequiredService<RunCoordinator>().CurrentMapName = name));
```

- [x] **Step 5: Write the run endpoint**

Add to `MapGeoValiApi` in `src/GeoVali/Web/ApiEndpoints.cs`:

```csharp
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
```

and the helper, as a private static member of `ApiEndpoints`:

```csharp
    /// <summary>Guards against a request naming a folder outside the user's maps tree.</summary>
    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return normalizedCandidate.Equals(normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
```

and the request body record in `src/GeoVali/Web/ApiModels.cs`:

```csharp
public sealed record RunRequestBody(string? scope, string? directory);
```

- [x] **Step 6: Write the Server-Sent Events endpoint**

Add to `MapGeoValiApi`:

```csharp
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
```

- [x] **Step 7: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: PASS, 17 tests.

- [x] **Step 8: Commit**

```bash
git add src/GeoVali/Web src/GeoVali/Running/UpdateRunner.cs tests/GeoVali.Tests/ApiEndpointTests.cs
git commit -m "feat: run endpoints and live progress over server-sent events"
```

---

### Task 16: First-run endpoints — folder browser and cookie validation

**Files:**
- Create: `src/GeoVali/Web/FolderBrowser.cs`
- Modify: `src/GeoVali/Web/ApiEndpoints.cs`
- Modify: `src/GeoVali/Web/ApiModels.cs`
- Test: `tests/GeoVali.Tests/FolderBrowserTests.cs`
- Modify: `tests/GeoVali.Tests/ApiEndpointTests.cs`

**Interfaces:**
- Consumes: `MapPaths`, `MapScanner` (Task 3), `ConfigStore`, `CredentialStore` (Task 9), `IGeoguessrClient` (Task 6).
- Produces:
  - `GeoVali.Web.FolderEntry` — `sealed record FolderEntry(string name, string path, bool hasMapJson, int mapCountBelow)`.
  - `GeoVali.Web.FolderListing` — `sealed record FolderListing(string? path, string? parent, IReadOnlyList<FolderEntry> entries)`.
  - `GeoVali.Web.FolderBrowser.List(string? path)` → `FolderListing`.
  - `GET /api/browse?path=` → `FolderListing`.
  - `POST /api/setup/folder` body `{ "path": "..." }` → `{ ok, mapCount }` or 400.
  - `POST /api/setup/cookie` body `{ "cookie": "..." }` → `{ ok, nick }` or 400 with a message.
  - `GeoVali.Web.FolderBody` — `sealed record FolderBody(string? path)`; `GeoVali.Web.CookieBody` — `sealed record CookieBody(string? cookie)`.

A browser cannot open a native directory dialog for a server, but the server is local, so GeoVali renders its own folder browser. Showing which directories actually contain maps is the point — it saves a non-developer from typing an absolute path and confirms the folder is the right one.

- [x] **Step 1: Write the failing folder-browser tests**

`tests/GeoVali.Tests/FolderBrowserTests.cs`:

```csharp
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
```

- [x] **Step 2: Write the failing setup-endpoint tests**

Append to `tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
    [Fact]
    public async Task Browse_lists_folders_and_flags_the_ones_holding_maps()
    {
        Directory.CreateDirectory(Path.Combine(MapsRoot, "africa"));
        await File.WriteAllTextAsync(Path.Combine(MapsRoot, "africa", MapPaths.DefinitionFileName), "{}");
        using var client = CreateClient();

        var listing = await client.GetFromJsonAsync<JsonNode>($"/api/browse?path={Uri.EscapeDataString(MapsRoot)}");

        var entry = listing!["entries"]!.AsArray().Single();
        Assert.Equal("africa", entry["name"]!.GetValue<string>());
        Assert.True(entry["hasMapJson"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Choosing_a_maps_folder_saves_it_and_reports_how_many_maps_are_in_it()
    {
        Directory.CreateDirectory(Path.Combine(MapsRoot, "africa"));
        await File.WriteAllTextAsync(Path.Combine(MapsRoot, "africa", MapPaths.DefinitionFileName), "{}");
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/setup/folder", new { path = MapsRoot }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal(1, body["mapCount"]!.GetValue<int>());
        Assert.Equal(MapsRoot, new ConfigStore(_temp.Path).Read().mapsRoot);
    }

    [Fact]
    public async Task Choosing_a_folder_that_does_not_exist_is_refused_with_a_readable_message()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/folder",
            new { path = Path.Combine(_temp.Path, "nope") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.Contains("does not exist", body!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_valid_cookie_is_saved_and_the_signed_in_name_confirmed()
    {
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "fresh-ncfa-value" }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal("slashP", body["nick"]!.GetValue<string>());
        Assert.Equal("fresh-ncfa-value",
            new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }

    [Fact]
    public async Task A_rejected_cookie_is_not_saved()
    {
        Geoguessr.Nick = null;
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "stale-value" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }

    [Fact]
    public async Task An_empty_cookie_is_refused()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_cookie_is_never_echoed_back_in_a_response()
    {
        using var client = CreateClient();
        await client.PostAsJsonAsync("/api/setup/cookie", new { cookie = "fresh-ncfa-value" });

        var status = await (await client.GetAsync("/api/status")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("fresh-ncfa-value", status);
    }
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~FolderBrowserTests|FullyQualifiedName~ApiEndpointTests"`
Expected: FAIL — `FolderBrowser` does not exist and the setup endpoints return 404.

- [x] **Step 4: Write `FolderBrowser`**

`src/GeoVali/Web/FolderBrowser.cs`:

```csharp
using GeoVali.Maps;

namespace GeoVali.Web;

public sealed record FolderEntry(string name, string path, bool hasMapJson, int mapCountBelow);

public sealed record FolderListing(string? path, string? parent, IReadOnlyList<FolderEntry> entries);

/// <summary>
/// A browser cannot open a native directory dialog for a server, so GeoVali renders its own
/// picker. Each row says whether that folder is a map and how many maps are under it, which is
/// how a non-developer confirms they picked the right tree.
/// </summary>
public static class FolderBrowser
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    public static FolderListing List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new FolderListing(null, null, Roots());
        }

        var full = Path.GetFullPath(path);

        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(full).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return new FolderListing(full, Path.GetDirectoryName(full), []);
        }

        var entries = children
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Select(directory => new FolderEntry(
                name: new DirectoryInfo(directory).Name,
                path: directory,
                hasMapJson: File.Exists(MapPaths.Definition(directory)),
                mapCountBelow: CountMaps(directory)))
            .ToList();

        return new FolderListing(full, Path.GetDirectoryName(full), entries);
    }

    private static int CountMaps(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, MapPaths.DefinitionFileName, Options).Count();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<FolderEntry> Roots()
    {
        var roots = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            roots.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName));
        }
        else
        {
            roots.Add("/");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            roots.Insert(0, home);
        }

        return roots
            .Distinct(StringComparer.Ordinal)
            .Select(root => new FolderEntry(root, root, File.Exists(MapPaths.Definition(root)), CountMaps(root)))
            .ToList();
    }
}
```

`CountMaps` on a filesystem root can be slow. It is only called for the roots listing and for the
children of one directory at a time, both of which the user triggers deliberately; the dashboard
shows a spinner while the request is in flight.

- [x] **Step 5: Write the setup endpoints**

Add to `MapGeoValiApi` in `src/GeoVali/Web/ApiEndpoints.cs`:

```csharp
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
```

and the bodies in `src/GeoVali/Web/ApiModels.cs`:

```csharp
public sealed record FolderBody(string? path);

public sealed record CookieBody(string? cookie);
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FolderBrowserTests|FullyQualifiedName~ApiEndpointTests"`
Expected: PASS, 31 tests.

- [x] **Step 7: Commit**

```bash
git add src/GeoVali/Web tests/GeoVali.Tests/FolderBrowserTests.cs tests/GeoVali.Tests/ApiEndpointTests.cs
git commit -m "feat: folder picker and cookie validation for first run"
```

---

### Task 17: Map setup endpoints — create new and link existing

**Files:**
- Modify: `src/GeoVali/Web/ApiEndpoints.cs`
- Modify: `src/GeoVali/Web/ApiModels.cs`
- Modify: `tests/GeoVali.Tests/ApiEndpointTests.cs`

**Interfaces:**
- Consumes: `MapUrlParser`, `AvatarGenerator`, `IGeoguessrClient.GetDraftAsync` (Task 6), `MapMetadataStore` (Task 4).
- Produces:
  - `POST /api/maps/create` body `{ "directory": "...", "name": "...", "description": "..." }` → `{ ok }`.
  - `POST /api/maps/link` body `{ "directory": "...", "url": "..." }` → `{ ok, name, id }` or 400.
  - `GeoVali.Web.CreateMapBody` — `sealed record CreateMapBody(string? directory, string? name, string? description)`.
  - `GeoVali.Web.LinkMapBody` — `sealed record LinkMapBody(string? directory, string? url)`.

Linking reuses `GET /api/v4/user-maps/drafts/{id}`, a call already in the publish path: a success proves the signed-in user owns the draft and yields the current name to seed `geoguessr.json`. It costs nothing extra.

- [x] **Step 1: Write the failing tests**

Append to `tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
    private async Task<string> AddUnconfiguredMap(string folderName)
    {
        var dir = Path.Combine(MapsRoot, folderName);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MapPaths.Definition(dir), "{}");
        Vali.LocationsByDirectory[dir] = FiveLocations;
        return dir;
    }

    [Fact]
    public async Task Creating_a_map_writes_geoguessr_json_with_a_generated_avatar_and_no_id()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("coastal");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create", new
        {
            directory = dir,
            name = "Coastal Sri Lanka",
            description = "{{LocationCount}} hand-picked coastal locations."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("Coastal Sri Lanka", metadata!.name);
        Assert.Equal("{{LocationCount}} hand-picked coastal locations.", metadata.description);
        Assert.Null(metadata.id);          // filled in on the first publish
        Assert.True(metadata.published);
        Assert.NotNull(metadata.avatar);
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar!.background));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.landscape));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.ground));
        Assert.False(string.IsNullOrWhiteSpace(metadata.avatar.decoration));
    }

    [Fact]
    public async Task Creating_a_map_refuses_a_folder_that_is_already_set_up()
    {
        ConfigureMapsRoot();
        var dir = await AddConfiguredMap("africa", "Africa");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = dir, name = "Africa Again", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Africa", (await MapMetadataStore.ReadMetadataAsync(dir))!.name);
    }

    [Fact]
    public async Task Creating_a_map_refuses_a_folder_outside_the_maps_root()
    {
        ConfigureMapsRoot();
        var outside = Path.Combine(_temp.Path, "elsewhere");
        Directory.CreateDirectory(outside);
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = outside, name = "Sneaky", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_map_refuses_an_empty_name()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("coastal");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/create",
            new { directory = dir, name = "  ", description = "d" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Linking_an_existing_map_seeds_geoguessr_json_from_the_draft()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        Geoguessr.Drafts["60a19170089ecc0001db6609"] =
            new DraftInfo("60a19170089ecc0001db6609", "An Arbitrary Africa", 306);
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/maps/link", new
        {
            directory = dir,
            url = "https://www.geoguessr.com/maps/60a19170089ecc0001db6609"
        })).Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["ok"]!.GetValue<bool>());
        Assert.Equal("An Arbitrary Africa", body["name"]!.GetValue<string>());

        var metadata = await MapMetadataStore.ReadMetadataAsync(dir);
        Assert.Equal("60a19170089ecc0001db6609", metadata!.id);
        Assert.Equal("An Arbitrary Africa", metadata.name);
        Assert.NotNull(metadata.avatar);
    }

    [Fact]
    public async Task Linking_refuses_a_url_that_is_not_a_geoguessr_map()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/maps/link",
            new { directory = dir, url = "https://example.com/whatever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(dir));
    }

    [Fact]
    public async Task Linking_refuses_a_map_the_signed_in_user_does_not_own()
    {
        ConfigureMapsRoot();
        var dir = await AddUnconfiguredMap("africa");
        using var client = CreateClient();

        // No draft registered, so GetDraftAsync returns null.
        var response = await client.PostAsJsonAsync("/api/maps/link", new
        {
            directory = dir,
            url = "https://www.geoguessr.com/maps/60a19170089ecc0001db6609"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.Contains("could not be found on your account", body!["error"]!.GetValue<string>());
        Assert.Null(await MapMetadataStore.ReadMetadataAsync(dir));
    }
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: FAIL — `/api/maps/create` and `/api/maps/link` return 404.

- [x] **Step 3: Write the endpoints**

Add to `MapGeoValiApi` in `src/GeoVali/Web/ApiEndpoints.cs`:

```csharp
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
```

and the shared validation helper, as a private static member of `ApiEndpoints`:

```csharp
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
```

and the bodies in `src/GeoVali/Web/ApiModels.cs`:

```csharp
public sealed record CreateMapBody(string? directory, string? name, string? description);

public sealed record LinkMapBody(string? directory, string? url);
```

- [x] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: PASS, 38 tests.

- [x] **Step 5: Commit**

```bash
git add src/GeoVali/Web tests/GeoVali.Tests/ApiEndpointTests.cs
git commit -m "feat: create a new map or link an existing one from the dashboard"
```

---

### Task 18: Autostart per OS and the settings endpoints

**Files:**
- Create: `src/GeoVali/Autostart/IAutostart.cs`
- Create: `src/GeoVali/Autostart/Autostart.cs`
- Modify: `src/GeoVali/Web/ApiEndpoints.cs`
- Modify: `src/GeoVali/Web/ApiModels.cs`
- Modify: `src/GeoVali/Web/ServiceRegistration.cs`
- Test: `tests/GeoVali.Tests/AutostartTests.cs`
- Modify: `tests/GeoVali.Tests/ApiEndpointTests.cs`

**Interfaces:**
- Consumes: `ConfigStore`, `CredentialStore` (Task 9), `Cadence` (Task 2).
- Produces:
  - `GeoVali.Autostart.IAutostart` — `bool IsEnabled()`, `void Enable()`, `void Disable()`, `string Describe()`.
  - `GeoVali.Autostart.WindowsAutostart`, `MacAutostart`, `LinuxAutostart`, `UnsupportedAutostart`, each `public X(string executablePath, string stateDirectory)`.
  - `GeoVali.Autostart.AutostartFactory.Create(string executablePath, string stateDirectory)` → `IAutostart`.
  - `GET /api/settings` → `{ mapsRoot, defaultCadenceDays, checkIntervalMinutes, dashboardPort, startAtLogin, autostartDescription, credentialProtectionNote }`.
  - `POST /api/settings` body `SettingsBody` → `{ ok, restartRequired }`.
  - `POST /api/settings/cookie/clear` → `{ ok }`.
  - `GeoVali.Web.SettingsBody` — `sealed record SettingsBody(string? mapsRoot, int? defaultCadenceDays, int? checkIntervalMinutes, int? dashboardPort, bool? startAtLogin)`.

`stateDirectory` is a constructor parameter rather than a hard-coded per-OS path so the tests write into a temp folder instead of the real Startup folder or LaunchAgents.

- [x] **Step 1: Write the failing autostart tests**

`tests/GeoVali.Tests/AutostartTests.cs`:

```csharp
using GeoVali.Autostart;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class AutostartTests
{
    [Fact]
    public void Mac_writes_and_removes_a_launch_agent_plist()
    {
        using var temp = new TempDir();
        var autostart = new MacAutostart("/usr/local/bin/geovali", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var plist = Path.Combine(temp.Path, "com.geovali.agent.plist");
        Assert.True(File.Exists(plist));
        var text = File.ReadAllText(plist);
        Assert.Contains("com.geovali.agent", text);
        Assert.Contains("/usr/local/bin/geovali", text);
        Assert.Contains("<key>RunAtLoad</key>", text);
        Assert.True(autostart.IsEnabled());

        autostart.Disable();

        Assert.False(File.Exists(plist));
        Assert.False(autostart.IsEnabled());
    }

    [Fact]
    public void Linux_writes_and_removes_a_systemd_user_unit()
    {
        using var temp = new TempDir();
        var autostart = new LinuxAutostart("/home/perhel/.dotnet/tools/geovali", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var unit = Path.Combine(temp.Path, "geovali.service");
        Assert.True(File.Exists(unit));
        var text = File.ReadAllText(unit);
        Assert.Contains("ExecStart=/home/perhel/.dotnet/tools/geovali", text);
        Assert.Contains("WantedBy=default.target", text);
        Assert.True(autostart.IsEnabled());

        autostart.Disable();

        Assert.False(File.Exists(unit));
    }

    [Fact]
    public void Windows_writes_and_removes_a_startup_folder_shortcut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // The .url file it writes is only meaningful on Windows.
        }

        using var temp = new TempDir();
        var autostart = new WindowsAutostart(@"C:\Users\perhel\.dotnet\tools\geovali.exe", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var shortcut = Path.Combine(temp.Path, "GeoVali.url");
        Assert.True(File.Exists(shortcut));
        Assert.Contains("geovali.exe", File.ReadAllText(shortcut));

        autostart.Disable();

        Assert.False(File.Exists(shortcut));
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        using var temp = new TempDir();
        var autostart = new LinuxAutostart("/bin/geovali", temp.Path);

        autostart.Enable();
        autostart.Enable();

        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Disable_on_something_never_enabled_is_a_no_op()
    {
        using var temp = new TempDir();
        new LinuxAutostart("/bin/geovali", temp.Path).Disable();
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Every_implementation_describes_what_it_does_in_plain_words()
    {
        using var temp = new TempDir();

        Assert.Contains("Startup folder", new WindowsAutostart("v", temp.Path).Describe());
        Assert.Contains("LaunchAgent", new MacAutostart("v", temp.Path).Describe());
        Assert.Contains("systemd", new LinuxAutostart("v", temp.Path).Describe());
        Assert.Contains("not supported", new UnsupportedAutostart().Describe());
    }

    [Fact]
    public void The_unsupported_implementation_never_throws()
    {
        var autostart = new UnsupportedAutostart();

        autostart.Enable();
        autostart.Disable();

        Assert.False(autostart.IsEnabled());
    }

    [Fact]
    public void The_factory_returns_something_usable_on_this_machine()
    {
        using var temp = new TempDir();
        Assert.NotNull(AutostartFactory.Create("geovali", temp.Path));
    }
}
```

- [x] **Step 2: Write the failing settings-endpoint tests**

Append to `tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
    [Fact]
    public async Task Settings_returns_the_current_values()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var settings = await client.GetFromJsonAsync<JsonNode>("/api/settings");

        Assert.Equal(MapsRoot, settings!["mapsRoot"]!.GetValue<string>());
        Assert.Equal(7, settings["defaultCadenceDays"]!.GetValue<int>());
        Assert.Equal(30, settings["checkIntervalMinutes"]!.GetValue<int>());
        Assert.Equal(5099, settings["dashboardPort"]!.GetValue<int>());
        Assert.False(settings["startAtLogin"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(settings["credentialProtectionNote"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Settings_saves_changed_values()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new
        {
            defaultCadenceDays = 10,
            checkIntervalMinutes = 60
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = new ConfigStore(_temp.Path).Read();
        Assert.Equal(10, saved.defaultCadenceDays);
        Assert.Equal(60, saved.checkIntervalMinutes);
        Assert.Equal(MapsRoot, saved.mapsRoot); // untouched fields survive
    }

    [Fact]
    public async Task Settings_refuses_a_cadence_below_one_day()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new { defaultCadenceDays = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(7, new ConfigStore(_temp.Path).Read().defaultCadenceDays);
    }

    [Fact]
    public async Task Settings_refuses_a_check_interval_below_one_minute()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings", new { checkIntervalMinutes = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_refuses_a_maps_root_that_does_not_exist()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/settings",
            new { mapsRoot = Path.Combine(_temp.Path, "nope") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_port_reports_that_a_restart_is_needed()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/settings", new { dashboardPort = 5150 }))
            .Content.ReadFromJsonAsync<JsonNode>();

        Assert.True(body!["restartRequired"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Clearing_the_cookie_removes_it()
    {
        ConfigureMapsRoot();
        using var client = CreateClient();

        await client.PostAsync("/api/settings/cookie/clear", null);

        Assert.Null(new CredentialStore(_temp.Path, CredentialProtectorFactory.Create()).ReadCookie());
    }
```

- [x] **Step 3: Run the tests and verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AutostartTests|FullyQualifiedName~ApiEndpointTests"`
Expected: FAIL — the autostart types and settings endpoints do not exist.

- [x] **Step 4: Write the autostart interface and implementations**

`src/GeoVali/Autostart/IAutostart.cs`:

```csharp
namespace GeoVali.Autostart;

public interface IAutostart
{
    bool IsEnabled();
    void Enable();
    void Disable();

    /// <summary>One sentence for the settings screen saying what enabling this actually does.</summary>
    string Describe();
}
```

`src/GeoVali/Autostart/Autostart.cs`:

```csharp
using System.Runtime.InteropServices;

namespace GeoVali.Autostart;

/// <summary>Windows: a .url shortcut in the per-user Startup folder. No registry, no admin rights.</summary>
public sealed class WindowsAutostart(string executablePath, string stateDirectory) : IAutostart
{
    private string ShortcutPath => Path.Combine(stateDirectory, "GeoVali.url");

    public bool IsEnabled() => File.Exists(ShortcutPath);

    public void Enable()
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(ShortcutPath, $"""
            [InternetShortcut]
            URL=file:///{executablePath.Replace('\\', '/')}
            IconIndex=0
            """);
    }

    public void Disable()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    public string Describe() =>
        "Adds a shortcut to your Startup folder, so GeoVali runs when you sign in to Windows.";
}

/// <summary>macOS: a LaunchAgent plist in ~/Library/LaunchAgents.</summary>
public sealed class MacAutostart(string executablePath, string stateDirectory) : IAutostart
{
    public const string Label = "com.geovali.agent";

    private string PlistPath => Path.Combine(stateDirectory, $"{Label}.plist");

    public bool IsEnabled() => File.Exists(PlistPath);

    public void Enable()
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(PlistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{Label}</string>
              <key>ProgramArguments</key>
              <array>
                <string>{executablePath}</string>
                <string>--no-browser</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>
            """);
    }

    public void Disable()
    {
        if (File.Exists(PlistPath))
        {
            File.Delete(PlistPath);
        }
    }

    public string Describe() =>
        "Installs a LaunchAgent in ~/Library/LaunchAgents, so GeoVali runs when you log in. " +
        "It takes effect at your next login, or after `launchctl load ~/Library/LaunchAgents/com.geovali.agent.plist`.";
}

/// <summary>Linux: a systemd user unit in ~/.config/systemd/user.</summary>
public sealed class LinuxAutostart(string executablePath, string stateDirectory) : IAutostart
{
    private string UnitPath => Path.Combine(stateDirectory, "geovali.service");

    public bool IsEnabled() => File.Exists(UnitPath);

    public void Enable()
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(UnitPath, $"""
            [Unit]
            Description=GeoVali - regenerate and republish vali GeoGuessr maps

            [Service]
            Type=simple
            ExecStart={executablePath} --no-browser
            Restart=on-failure

            [Install]
            WantedBy=default.target
            """);
    }

    public void Disable()
    {
        if (File.Exists(UnitPath))
        {
            File.Delete(UnitPath);
        }
    }

    public string Describe() =>
        "Installs a systemd user unit in ~/.config/systemd/user. " +
        "Activate it with `systemctl --user enable --now geovali`.";
}

/// <summary>Anything else. Never throws; the settings toggle simply stays off.</summary>
public sealed class UnsupportedAutostart : IAutostart
{
    public bool IsEnabled() => false;
    public void Enable() { }
    public void Disable() { }
    public string Describe() => "Start at login is not supported on this platform.";
}

public static class AutostartFactory
{
    /// <summary>The per-OS directory the unit or shortcut belongs in.</summary>
    public static string DefaultStateDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "LaunchAgents");
        }

        return Path.Combine(home, ".config", "systemd", "user");
    }

    /// <summary>The path of the running geovali executable, for the unit's ExecStart.</summary>
    public static string CurrentExecutablePath() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GeoVali");

    public static IAutostart Create(string executablePath, string stateDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsAutostart(executablePath, stateDirectory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacAutostart(executablePath, stateDirectory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxAutostart(executablePath, stateDirectory);
        }

        return new UnsupportedAutostart();
    }
}
```

- [x] **Step 5: Register autostart**

Add to `AddGeoVali` in `src/GeoVali/Web/ServiceRegistration.cs`, before `return services;`:

```csharp
        services.AddSingleton<IAutostart>(_ => AutostartFactory.Create(
            AutostartFactory.CurrentExecutablePath(),
            AutostartFactory.DefaultStateDirectory()));
```

with `using GeoVali.Autostart;` at the top.

- [x] **Step 6: Write the settings endpoints**

Add `using GeoVali.Autostart;` to the top of `src/GeoVali/Web/ApiEndpoints.cs`, then add to
`MapGeoValiApi`:

```csharp
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

            var updated = current with
            {
                mapsRoot = body.mapsRoot is null ? current.mapsRoot : Path.GetFullPath(body.mapsRoot),
                defaultCadenceDays = body.defaultCadenceDays ?? current.defaultCadenceDays,
                checkIntervalMinutes = body.checkIntervalMinutes ?? current.checkIntervalMinutes,
                dashboardPort = body.dashboardPort ?? current.dashboardPort,
                startAtLogin = body.startAtLogin ?? current.startAtLogin
            };

            config.Write(updated);

            if (body.startAtLogin is not null)
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
```

and the body in `src/GeoVali/Web/ApiModels.cs`:

```csharp
public sealed record SettingsBody(
    string? mapsRoot,
    int? defaultCadenceDays,
    int? checkIntervalMinutes,
    int? dashboardPort,
    bool? startAtLogin);
```

- [x] **Step 7: Run the tests and verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AutostartTests|FullyQualifiedName~ApiEndpointTests"`
Expected: PASS, 53 tests.

Note: the settings endpoint tests resolve the real `IAutostart`. Nothing in them flips `startAtLogin`, so no unit or shortcut is written to the developer's machine. Do not add a test that posts `startAtLogin = true` through the HTTP layer — cover that through `AutostartTests` with a temp directory instead.

- [x] **Step 8: Commit**

```bash
git add src/GeoVali/Autostart src/GeoVali/Web tests/GeoVali.Tests/AutostartTests.cs \
        tests/GeoVali.Tests/ApiEndpointTests.cs
git commit -m "feat: start at login per OS and the settings endpoints"
```

---

### Task 19: The dashboard

**Files:**
- Create: `src/GeoVali/wwwroot/index.html`
- Create: `src/GeoVali/wwwroot/style.css`
- Create: `src/GeoVali/wwwroot/app.js`
- Modify: `src/GeoVali/GeoVali.csproj` (embed wwwroot)
- Modify: `src/GeoVali/Program.cs` (serve the embedded files)
- Modify: `tests/GeoVali.Tests/ApiEndpointTests.cs`

**Interfaces:**
- Consumes: every endpoint from Tasks 14–18.
- Produces: `GET /` serving the embedded `index.html`.

The page is static HTML with vanilla JavaScript, served from resources embedded in the assembly. No build step keeps the global tool a single package with no npm, and the page can be opened and read directly.

- [x] **Step 1: Write the failing test**

Append to `tests/GeoVali.Tests/ApiEndpointTests.cs`:

```csharp
    [Fact]
    public async Task The_dashboard_page_is_served_from_the_embedded_resources()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("GeoVali", html);
        Assert.Contains("app.js", html);
    }

    [Fact]
    public async Task The_dashboard_script_and_stylesheet_are_served_too()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/style.css")).StatusCode);
    }
```

- [x] **Step 2: Run the test and verify it fails**

Run: `dotnet test --filter FullyQualifiedName~The_dashboard`
Expected: FAIL — 404.

- [x] **Step 3: Embed wwwroot in the assembly**

```bash
dotnet add src/GeoVali/GeoVali.csproj package Microsoft.Extensions.FileProviders.Embedded
```

Add to `src/GeoVali/GeoVali.csproj`:

```xml
  <PropertyGroup>
    <GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>
```

- [x] **Step 4: Serve the embedded files**

In `src/GeoVali/Program.cs`, between `app.UseLocalOnly();` and `app.MapGeoValiApi();`:

```csharp
var embedded = new Microsoft.Extensions.FileProviders.ManifestEmbeddedFileProvider(
    typeof(Program).Assembly, "wwwroot");

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embedded });
app.UseStaticFiles(new StaticFileOptions { FileProvider = embedded });
```

- [x] **Step 5: Write the page**

`src/GeoVali/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>GeoVali</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <header>
    <h1>GeoVali</h1>
    <div id="header-facts" class="facts"></div>
  </header>

  <div id="banner" class="banner" hidden></div>

  <!-- First run, screen 1: choose the maps folder. -->
  <section id="screen-folder" hidden>
    <h2>Choose your maps folder</h2>
    <p>Pick the folder that holds your vali map definitions. A folder is a map when it contains
      <code>map.json</code>.</p>
    <p id="browse-current" class="path"></p>
    <ul id="browse-list" class="browse"></ul>
    <button id="browse-choose" type="button">Use this folder</button>
  </section>

  <!-- First run, screen 2: paste the cookie. -->
  <section id="screen-cookie" hidden>
    <h2>Connect your GeoGuessr account</h2>
    <div class="two-column">
      <ol class="walkthrough">
        <li>Open <a href="https://www.geoguessr.com" target="_blank" rel="noreferrer">geoguessr.com</a> and sign in.</li>
        <li>Press <kbd>F12</kbd> to open developer tools.</li>
        <li>Go to <strong>Application</strong>, then <strong>Cookies</strong>, then <strong>geoguessr.com</strong>.</li>
        <li>Find <code>_ncfa</code> and copy its whole value.</li>
        <li>Paste it here.</li>
      </ol>
      <div>
        <label for="cookie-input">_ncfa value</label>
        <textarea id="cookie-input" rows="4" spellcheck="false"></textarea>
        <button id="cookie-save" type="button">Save and check</button>
        <p id="cookie-message" class="message"></p>
      </div>
    </div>
  </section>

  <!-- The dashboard. -->
  <section id="screen-dashboard" hidden>
    <div class="actions">
      <button id="run-due" type="button">Run all due</button>
      <button id="run-all" type="button">Run everything</button>
      <button id="open-settings" type="button" class="secondary">Settings</button>
    </div>

    <table id="maps">
      <thead>
        <tr>
          <th>Map</th><th>Every</th><th>Last published</th><th>Updates</th><th>Status</th><th></th>
        </tr>
      </thead>
      <tbody id="maps-body"></tbody>
    </table>

    <h2>Activity</h2>
    <pre id="live" class="live"></pre>
  </section>

  <!-- Settings. -->
  <section id="screen-settings" hidden>
    <h2>Settings</h2>
    <label>Default cadence (days)<input id="set-cadence" type="number" min="1"></label>
    <label>Check interval (minutes)<input id="set-interval" type="number" min="1"></label>
    <label>Dashboard port<input id="set-port" type="number" min="1024" max="65535"></label>
    <label class="checkbox"><input id="set-autostart" type="checkbox"> Start at login</label>
    <p id="autostart-description" class="hint"></p>
    <label>Maps folder<input id="set-maps-root" type="text"></label>
    <p id="credential-note" class="hint"></p>
    <div class="actions">
      <button id="settings-save" type="button">Save</button>
      <button id="settings-cookie" type="button" class="secondary">Paste a new cookie</button>
      <button id="settings-back" type="button" class="secondary">Back</button>
    </div>
    <p id="settings-message" class="message"></p>
  </section>

  <!-- Set up a map that has map.json but no geoguessr.json. -->
  <dialog id="setup-dialog">
    <h2 id="setup-title">Set up this map</h2>
    <div class="tabs">
      <button id="tab-create" type="button">Create new map</button>
      <button id="tab-link" type="button">Link existing</button>
    </div>
    <div id="pane-create">
      <label>Name<input id="create-name" type="text"></label>
      <label>Description<textarea id="create-description" rows="3"></textarea></label>
      <p class="hint">Write <code>{{LocationCount}}</code> where you want the number of locations.</p>
    </div>
    <div id="pane-link" hidden>
      <label>GeoGuessr map link<input id="link-url" type="text" placeholder="https://www.geoguessr.com/maps/..."></label>
    </div>
    <p id="setup-message" class="message"></p>
    <div class="actions">
      <button id="setup-save" type="button">Save</button>
      <button id="setup-cancel" type="button" class="secondary">Cancel</button>
    </div>
  </dialog>

  <script src="app.js"></script>
</body>
</html>
```

`src/GeoVali/wwwroot/style.css`:

```css
:root {
  --ink: #1c1f23;
  --muted: #5c6570;
  --line: #d9dee4;
  --accent: #1f6feb;
  --bad: #b42318;
  --good: #067647;
  --paper: #ffffff;
  --wash: #f6f8fa;
}

* { box-sizing: border-box; }

body {
  margin: 0;
  font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
  color: var(--ink);
  background: var(--wash);
}

header {
  display: flex;
  align-items: baseline;
  gap: 1.5rem;
  padding: 1rem 1.5rem;
  background: var(--paper);
  border-bottom: 1px solid var(--line);
}

h1 { font-size: 1.1rem; margin: 0; letter-spacing: 0.02em; }
h2 { font-size: 1rem; margin: 1.5rem 0 0.5rem; }

.facts { color: var(--muted); font-size: 0.85rem; display: flex; gap: 1.25rem; flex-wrap: wrap; }

section { max-width: 60rem; margin: 0 auto; padding: 1.5rem; }

.banner {
  background: #fff4e5;
  border-bottom: 1px solid #f0c48a;
  padding: 0.75rem 1.5rem;
  color: #7a4100;
}

button {
  font: inherit;
  padding: 0.4rem 0.9rem;
  border: 1px solid var(--accent);
  background: var(--accent);
  color: white;
  border-radius: 6px;
  cursor: pointer;
}

button.secondary { background: var(--paper); color: var(--ink); border-color: var(--line); }
button:disabled { opacity: 0.5; cursor: default; }

.actions { display: flex; gap: 0.6rem; margin: 1rem 0; flex-wrap: wrap; }

table { width: 100%; border-collapse: collapse; background: var(--paper); border: 1px solid var(--line); }
th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--line); }
th { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); }
tr:last-child td { border-bottom: none; }

.status-ok { color: var(--good); }
.status-bad { color: var(--bad); }
.status-due { color: var(--accent); }
.status-new { color: var(--muted); }

.live {
  background: #10141a;
  color: #d7dce3;
  padding: 0.75rem;
  height: 16rem;
  overflow-y: auto;
  border-radius: 6px;
  font: 12px/1.5 ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  white-space: pre-wrap;
}

.browse { list-style: none; padding: 0; margin: 0.5rem 0; max-height: 20rem; overflow-y: auto;
          background: var(--paper); border: 1px solid var(--line); border-radius: 6px; }
.browse li { padding: 0.4rem 0.75rem; border-bottom: 1px solid var(--line); cursor: pointer; display: flex; gap: 0.75rem; }
.browse li:hover { background: var(--wash); }
.browse .count { color: var(--muted); font-size: 0.85rem; margin-left: auto; }

.path { font: 12px ui-monospace, monospace; color: var(--muted); word-break: break-all; }
.two-column { display: grid; grid-template-columns: 1fr 1fr; gap: 2rem; }
.walkthrough { padding-left: 1.2rem; }
.hint { color: var(--muted); font-size: 0.85rem; }
.message { min-height: 1.4rem; }
.message.error { color: var(--bad); }
.message.ok { color: var(--good); }

label { display: block; margin: 0.75rem 0; }
label.checkbox { display: flex; align-items: center; gap: 0.5rem; }
input[type=text], input[type=number], textarea {
  display: block; width: 100%; font: inherit; padding: 0.4rem;
  border: 1px solid var(--line); border-radius: 6px; margin-top: 0.25rem;
}
input[type=checkbox] { width: auto; margin: 0; }

dialog { border: 1px solid var(--line); border-radius: 8px; padding: 1.5rem; max-width: 32rem; width: 90%; }
.tabs { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
```

`src/GeoVali/wwwroot/app.js`:

```javascript
"use strict";

const HEADERS = { "Content-Type": "application/json", "X-GeoVali": "1" };

const $ = (id) => document.getElementById(id);

const state = {
  browsePath: null,
  setupDirectory: null,
  setupMode: "create",
  running: false
};

async function get(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${response.status}`);
  return response.json();
}

async function post(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: HEADERS,
    body: body === undefined ? null : JSON.stringify(body)
  });
  const text = await response.text();
  const parsed = text ? JSON.parse(text) : {};
  if (!response.ok) throw new Error(parsed.error || `Request failed (${response.status}).`);
  return parsed;
}

function show(screen) {
  for (const id of ["screen-folder", "screen-cookie", "screen-dashboard", "screen-settings"]) {
    $(id).hidden = id !== screen;
  }
}

function formatDate(value) {
  if (!value) return "never";
  return new Date(value).toLocaleString();
}

// ---- Status and routing ----

async function refreshStatus() {
  const status = await get("/api/status");
  state.running = status.running;

  $("header-facts").textContent = [
    status.mapsRoot ? `Maps: ${status.mapsRoot}` : "No maps folder chosen",
    status.userNick ? `Signed in as ${status.userNick}` : "Not signed in",
    status.running
      ? `Running${status.currentMap ? `: ${status.currentMap}` : ""}`
      : status.nextRunUtc ? `Next check ${formatDate(status.nextRunUtc)}` : "Scheduler idle",
    `v${status.version}`
  ].join("   ");

  const banner = $("banner");
  if (!status.valiInstalled) {
    banner.hidden = false;
    banner.textContent = `vali was not found on your PATH. Install it with:  ${status.valiInstallCommand}`;
  } else if (status.setupComplete && !status.authValid) {
    banner.hidden = false;
    banner.textContent = "Your GeoGuessr sign-in has expired. Open Settings and paste a fresh _ncfa cookie value.";
  } else {
    banner.hidden = true;
  }

  if (!status.mapsRoot) {
    show("screen-folder");
    await loadBrowse(null);
  } else if (!status.setupComplete) {
    show("screen-cookie");
  } else if ($("screen-settings").hidden) {
    show("screen-dashboard");
    await refreshMaps();
  }

  for (const id of ["run-due", "run-all"]) {
    $(id).disabled = state.running;
  }
  return status;
}

// ---- Folder picker ----

async function loadBrowse(path) {
  const listing = await get("/api/browse" + (path ? `?path=${encodeURIComponent(path)}` : ""));
  state.browsePath = listing.path;
  $("browse-current").textContent = listing.path || "Pick a drive or folder";
  $("browse-choose").disabled = !listing.path;

  const list = $("browse-list");
  list.replaceChildren();

  if (listing.parent) {
    const up = document.createElement("li");
    up.textContent = "..";
    up.onclick = () => loadBrowse(listing.parent);
    list.append(up);
  }

  for (const entry of listing.entries) {
    const item = document.createElement("li");
    const name = document.createElement("span");
    name.textContent = entry.hasMapJson ? `${entry.name} (a map)` : entry.name;
    const count = document.createElement("span");
    count.className = "count";
    count.textContent = entry.mapCountBelow > 0 ? `${entry.mapCountBelow} maps` : "";
    item.append(name, count);
    item.onclick = () => loadBrowse(entry.path);
    list.append(item);
  }
}

$("browse-choose").onclick = async () => {
  try {
    const result = await post("/api/setup/folder", { path: state.browsePath });
    if (result.mapCount === 0 && !confirm("No map.json was found in that folder. Use it anyway?")) {
      return;
    }
    await refreshStatus();
  } catch (e) {
    alert(e.message);
  }
};

// ---- Cookie ----

async function saveCookie() {
  const message = $("cookie-message");
  message.className = "message";
  message.textContent = "Checking...";
  try {
    const result = await post("/api/setup/cookie", { cookie: $("cookie-input").value });
    message.className = "message ok";
    message.textContent = `Signed in as ${result.nick}.`;
    $("cookie-input").value = "";
    await refreshStatus();
  } catch (e) {
    message.className = "message error";
    message.textContent = e.message;
  }
}

$("cookie-save").onclick = saveCookie;

// ---- Maps table ----

async function refreshMaps() {
  const maps = await get("/api/maps");
  const body = $("maps-body");
  body.replaceChildren();

  for (const map of maps) {
    const row = document.createElement("tr");

    const name = document.createElement("td");
    name.textContent = map.name + (map.configured && !map.published ? " (draft)" : "");
    row.append(name);

    const cadence = document.createElement("td");
    cadence.textContent = map.configured ? `${map.cadenceDays} days` : "";
    row.append(cadence);

    const last = document.createElement("td");
    last.textContent = map.configured ? formatDate(map.lastPublishedTimeUtc) : "";
    row.append(last);

    const updates = document.createElement("td");
    updates.textContent = map.configured ? map.updateCount : "";
    row.append(updates);

    const status = document.createElement("td");
    if (!map.configured) {
      status.className = "status-new";
      status.textContent = "not set up yet";
    } else if (map.lastError) {
      status.className = "status-bad";
      status.textContent = map.lastError;
    } else if (map.due) {
      status.className = "status-due";
      status.textContent = "due";
    } else {
      status.className = "status-ok";
      status.textContent = "up to date";
    }
    row.append(status);

    const action = document.createElement("td");
    const button = document.createElement("button");
    button.className = "secondary";
    if (map.configured) {
      button.textContent = "Run now";
      button.disabled = state.running;
      button.onclick = () => runScope("single", map.directory);
    } else {
      button.textContent = "Set up";
      button.onclick = () => openSetup(map.directory, map.folderName);
    }
    action.append(button);
    row.append(action);

    body.append(row);
  }
}

// ---- Runs ----

async function runScope(scope, directory) {
  try {
    const result = await post("/api/run", { scope, directory });
    if (result.preflightFailed) {
      alert(result.preflightError);
    }
  } catch (e) {
    alert(e.message);
  } finally {
    await refreshStatus();
    await refreshMaps();
  }
}

$("run-due").onclick = () => runScope("due");
$("run-all").onclick = () => runScope("all");

// ---- Live activity ----

function startEventStream() {
  const live = $("live");
  const source = new EventSource("/api/events");
  source.onmessage = (event) => {
    const entry = JSON.parse(event.data);
    const line = document.createElement("div");
    line.textContent = `${new Date(entry.timestampUtc).toLocaleTimeString()}  ${entry.message}`;
    if (entry.level === "error") line.style.color = "#ff9c8a";
    live.append(line);
    live.scrollTop = live.scrollHeight;
    while (live.childElementCount > 500) live.firstElementChild.remove();
  };
  source.onerror = () => {
    // EventSource reconnects on its own; nothing to do.
  };
}

// ---- Map setup dialog ----

function openSetup(directory, folderName) {
  state.setupDirectory = directory;
  state.setupMode = "create";
  $("setup-title").textContent = `Set up ${folderName}`;
  $("create-name").value = folderName;
  $("create-description").value = "{{LocationCount}} locations.";
  $("link-url").value = "";
  $("setup-message").textContent = "";
  $("pane-create").hidden = false;
  $("pane-link").hidden = true;
  $("setup-dialog").showModal();
}

$("tab-create").onclick = () => {
  state.setupMode = "create";
  $("pane-create").hidden = false;
  $("pane-link").hidden = true;
};

$("tab-link").onclick = () => {
  state.setupMode = "link";
  $("pane-create").hidden = true;
  $("pane-link").hidden = false;
};

$("setup-cancel").onclick = () => $("setup-dialog").close();

$("setup-save").onclick = async () => {
  const message = $("setup-message");
  message.className = "message";
  try {
    if (state.setupMode === "create") {
      await post("/api/maps/create", {
        directory: state.setupDirectory,
        name: $("create-name").value,
        description: $("create-description").value
      });
    } else {
      await post("/api/maps/link", {
        directory: state.setupDirectory,
        url: $("link-url").value
      });
    }
    $("setup-dialog").close();
    await refreshMaps();
  } catch (e) {
    message.className = "message error";
    message.textContent = e.message;
  }
};

// ---- Settings ----

$("open-settings").onclick = async () => {
  const settings = await get("/api/settings");
  $("set-cadence").value = settings.defaultCadenceDays;
  $("set-interval").value = settings.checkIntervalMinutes;
  $("set-port").value = settings.dashboardPort;
  $("set-autostart").checked = settings.startAtLogin;
  $("set-maps-root").value = settings.mapsRoot || "";
  $("autostart-description").textContent = settings.autostartDescription;
  $("credential-note").textContent = settings.credentialProtectionNote;
  $("settings-message").textContent = "";
  show("screen-settings");
};

$("settings-back").onclick = async () => {
  show("screen-dashboard");
  await refreshStatus();
};

$("settings-cookie").onclick = () => {
  show("screen-cookie");
};

$("settings-save").onclick = async () => {
  const message = $("settings-message");
  message.className = "message";
  try {
    const result = await post("/api/settings", {
      defaultCadenceDays: Number($("set-cadence").value),
      checkIntervalMinutes: Number($("set-interval").value),
      dashboardPort: Number($("set-port").value),
      startAtLogin: $("set-autostart").checked,
      mapsRoot: $("set-maps-root").value || null
    });
    message.className = "message ok";
    message.textContent = result.restartRequired
      ? "Saved. Restart geovali for the new port to take effect."
      : "Saved.";
  } catch (e) {
    message.className = "message error";
    message.textContent = e.message;
  }
};

// ---- Boot ----

startEventStream();
refreshStatus();
setInterval(() => {
  if ($("screen-dashboard").hidden) return;
  refreshStatus().then(refreshMaps).catch(() => {});
}, 5000);
```

- [x] **Step 6: Run the tests and verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ApiEndpointTests`
Expected: PASS, 55 tests.

- [x] **Step 7: See it in a browser**

```bash
dotnet run --project src/GeoVali
```

Open the printed URL. Confirm by hand: the folder picker lists directories with map counts; choosing
one advances to the cookie screen; the cookie walkthrough is legible; the dashboard table renders;
a folder without `geoguessr.json` shows "not set up yet" with a **Set up** button. Stop with
<kbd>Ctrl</kbd>+<kbd>C</kbd>.

- [x] **Step 8: Commit**

```bash
git add src/GeoVali/wwwroot src/GeoVali/GeoVali.csproj src/GeoVali/Program.cs \
        tests/GeoVali.Tests/ApiEndpointTests.cs
git commit -m "feat: embedded dashboard with first-run screens and live progress"
```

---

### Task 20: Startup — port selection, browser launch, packaging and README

**Files:**
- Modify: `src/GeoVali/Program.cs`
- Create: `src/GeoVali/Startup/PortPicker.cs`
- Create: `src/GeoVali/Startup/BrowserLauncher.cs`
- Create: `README.md`
- Test: `tests/GeoVali.Tests/PortPickerTests.cs`

**Interfaces:**
- Consumes: `AppConfig`, `ConfigStore`, `AppPaths` (Task 9).
- Produces:
  - `GeoVali.Startup.PortPicker.FindFree(int preferred, int attempts = 20)` → `int`.
  - `GeoVali.Startup.BrowserLauncher.Open(string url)` → `void`.
  - `geovali --no-browser` suppresses opening a browser (used by the autostart units from Task 18).

- [x] **Step 1: Write the failing port tests**

`tests/GeoVali.Tests/PortPickerTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using GeoVali.Startup;
using Xunit;

namespace GeoVali.Tests;

public class PortPickerTests
{
    [Fact]
    public void Returns_the_preferred_port_when_it_is_free()
    {
        var free = FindAnUnusedPort();

        Assert.Equal(free, PortPicker.FindFree(free));
    }

    [Fact]
    public void Steps_to_the_next_port_when_the_preferred_one_is_taken()
    {
        var taken = FindAnUnusedPort();
        using var listener = new TcpListener(IPAddress.Loopback, taken);
        listener.Start();

        var chosen = PortPicker.FindFree(taken);

        Assert.NotEqual(taken, chosen);
        Assert.InRange(chosen, taken + 1, taken + 20);
    }

    [Fact]
    public void Throws_a_readable_error_when_nothing_in_the_range_is_free()
    {
        var first = FindAnUnusedPort();
        var listeners = new List<TcpListener>();
        try
        {
            for (var port = first; port < first + 3; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listeners.Add(listener);
            }

            var exception = Assert.Throws<InvalidOperationException>(() => PortPicker.FindFree(first, attempts: 3));

            Assert.Contains("No free port", exception.Message);
            Assert.Contains(first.ToString(), exception.Message);
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }
        }
    }

    private static int FindAnUnusedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
```

- [x] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FullyQualifiedName~PortPickerTests`
Expected: FAIL — `PortPicker` does not exist.

- [x] **Step 3: Write `PortPicker`**

`src/GeoVali/Startup/PortPicker.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace GeoVali.Startup;

/// <summary>
/// The dashboard wants a stable port so the user can bookmark it, but must still start when
/// something else has claimed it. Try the configured port, then walk upwards.
/// </summary>
public static class PortPicker
{
    public static int FindFree(int preferred, int attempts = 20)
    {
        for (var port = preferred; port < preferred + attempts && port <= 65535; port++)
        {
            if (IsFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"No free port was found between {preferred} and {preferred + attempts - 1}. " +
            "Change the dashboard port in config.json and start GeoVali again.");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
```

- [x] **Step 4: Write `BrowserLauncher`**

`src/GeoVali/Startup/BrowserLauncher.cs`:

```csharp
using System.Diagnostics;

namespace GeoVali.Startup;

public static class BrowserLauncher
{
    /// <summary>
    /// Opens the dashboard in the default browser. Never throws: a headless machine or a missing
    /// xdg-open must not stop the server, which is still reachable at the printed URL.
    /// </summary>
    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }

            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(opener, url) { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException or PlatformNotSupportedException)
        {
            Console.WriteLine($"Open {url} in your browser.");
        }
    }
}
```

- [x] **Step 5: Finish `Program.cs`**

`src/GeoVali/Program.cs`:

```csharp
using Microsoft.Extensions.FileProviders;
using GeoVali;
using GeoVali.Configuration;
using GeoVali.Startup;
using GeoVali.Web;

var noBrowser = args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);

// Tests point this at a temp folder; normally it is the per-OS application data location.
var configDirectory = builder.Configuration["GeoVali:ConfigDirectory"] ?? AppPaths.ConfigDirectory;
Directory.CreateDirectory(configDirectory);

builder.Services.AddGeoVali(configDirectory);

// WebApplicationFactory supplies its own server, so only bind a real port outside tests.
var isTestHost = builder.Configuration["GeoVali:ConfigDirectory"] is not null;
var url = "";
if (!isTestHost)
{
    var preferred = new ConfigStore(configDirectory).Read().dashboardPort;
    var port = PortPicker.FindFree(preferred);
    url = $"http://127.0.0.1:{port}";
    builder.WebHost.UseUrls(url);
}

var app = builder.Build();

var embedded = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");

app.UseLocalOnly();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embedded });
app.UseStaticFiles(new StaticFileOptions { FileProvider = embedded });
app.MapGeoValiApi();

if (!isTestHost)
{
    Console.WriteLine($"{AppInfo.ProductName} {AppInfo.Version} — dashboard at {url}");
    Console.WriteLine("Press Ctrl+C to quit.");
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (!noBrowser)
        {
            BrowserLauncher.Open(url);
        }
    });
}

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
```

- [x] **Step 6: Write the README**

`README.md`:

````markdown
# GeoVali

Regenerates your [vali](https://github.com/slashP/vali) GeoGuessr maps on a schedule and
republishes them, from a local web dashboard.

## Install

You need the .NET 10 SDK, which you already have if you installed vali.

```bash
dotnet tool install -g vali        # if you have not already
dotnet tool install -g GeoVali
```

## Run

```bash
geovali
```

It starts a small web server on your own machine, opens the dashboard in your browser, and keeps
running until you quit it with Ctrl+C. Use `geovali --no-browser` to start it without opening a
browser.

On first run it asks two things:

1. **Which folder holds your maps.** A folder is a map when it contains `map.json`. Point GeoVali
   at the folder above them and it finds every map underneath.
2. **Your GeoGuessr `_ncfa` cookie.** In your browser, press F12, open **Application**, then
   **Cookies**, then **geoguessr.com**, find `_ncfa`, and copy its whole value.

## A map is a folder

| File | Written by | Commit it? |
|---|---|---|
| `map.json` | you — the vali map definition | yes |
| `geoguessr.json` | GeoVali at setup, then you by hand | yes |
| `geoguessr.ephemeral.json` | GeoVali every run | no |
| `map-locations.json` | `vali generate` | no |

Add these two lines to your maps repository's `.gitignore`. GeoVali never edits it for you:

```gitignore
geoguessr.ephemeral.json
map-locations.json
```

### `geoguessr.json`

```json
{
  "id": "6a9d6c505a43d0a64f98be5c",
  "name": "Coastal Sri Lanka",
  "description": "{{LocationCount}} hand-picked coastal locations.",
  "avatar": {
    "background": "evening",
    "landscape": "skyline",
    "ground": "yellow",
    "decoration": "japanese"
  },
  "published": true,
  "updateFrequencyDays": 10
}
```

- `id` — appears after the first publish. Leave it alone.
- `description` — `{{LocationCount}}` is replaced with the real number when the map is published.
  The token itself stays in the file.
- `published` — set to `false` to keep updating the draft without publishing it.
- `updateFrequencyDays` — how often this map regenerates. Remove it to use the global default.
- `avatar` — generated once and then kept, so your map thumbnail does not change every run.

To rename a map or change its description, edit this file. GeoVali does not edit map metadata for
you after setup.

## How a run works

Every 30 minutes (configurable) GeoVali checks which maps are due. Before touching anything it
confirms that `vali` is installed and that your cookie still works — a stale cookie stops the run
before any time is spent regenerating.

Then, one map at a time: run `vali generate`, read the locations, publish to GeoGuessr, and record
the result. A map is only marked as published when it actually succeeded, so a failed map stays due
and is retried from scratch on the next run. One bad map never stops the others.

## Where things are stored

| | |
|---|---|
| Windows | `%APPDATA%\GeoVali\` |
| macOS | `~/Library/Application Support/GeoVali/` |
| Linux | `$XDG_CONFIG_HOME/geovali/` or `~/.config/geovali/` |

`config.json` holds your settings. `credentials.json` holds only the cookie, kept separate so
nothing else ever prints it. On Windows it is encrypted with DPAPI for your account. On macOS and
Linux it is a file only your user can read (mode 0600) — **it is not encrypted**. If that matters
to you, sign out of GeoGuessr to revoke the cookie. `logs/` holds a rolling log, pruned after 14
days. The cookie never appears in it.

## Start at login

Settings has a toggle. It writes a Startup-folder shortcut on Windows, a LaunchAgent on macOS, and
a systemd user unit on Linux. On Linux, activate it with `systemctl --user enable --now geovali`.

## What it deliberately does not do

- Install or update vali, or download vali's country data. It only tells you the command.
- Edit map names, descriptions or tags after setup. Edit `geoguessr.json` by hand.
- Browse your existing GeoGuessr maps. Paste a map's link to connect it to a folder.
````

- [x] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything green.

- [x] **Step 8: Verify the packaged tool actually installs and runs**

```bash
dotnet pack src/GeoVali/GeoVali.csproj -c Release
dotnet tool install -g GeoVali --add-source ./nupkg
geovali --no-browser
```

Expected: it prints `GeoVali 0.1.0 — dashboard at http://127.0.0.1:5099` and serves the dashboard
at that address. Stop it with Ctrl+C and uninstall the test copy:

```bash
dotnet tool uninstall -g GeoVali
```

- [x] **Step 9: Commit**

```bash
git add src/GeoVali/Startup src/GeoVali/Program.cs README.md tests/GeoVali.Tests/PortPickerTests.cs
git commit -m "feat: port selection, browser launch and packaging"
```
