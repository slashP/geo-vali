# GeoVali — automated recreation and republishing of vali GeoGuessr maps

Date: 2026-09-06
Status: Approved design, ready for implementation planning

## 1. Purpose

Tech-literate non-developers who build GeoGuessr maps with
[vali](https://github.com/slashP/vali) currently regenerate and republish
those maps by hand. GeoVali watches a folder of vali map definitions and,
on a configurable per-map cadence, regenerates each map with vali and
republishes it to GeoGuessr.

It is a deliberately reduced version of the author's private
`GeoguessrMapCreator`/`ValiMapCreator` pipeline: the same core loop, none of
the personal infrastructure.

### Audience constraints

Users are comfortable installing a CLI tool and editing a JSON file. They are
not developers. Every failure must name what went wrong and what to do about
it. No stack traces as a primary error surface.

## 2. Scope

**In scope**

- Discover vali map definitions in a user-selected folder tree
- Regenerate locations by invoking the `vali` CLI
- Publish and republish maps to GeoGuessr, including creating brand-new maps
- Link a local definition to a map that already exists on the user's account
- Per-map update cadence with a global default
- Background scheduling with start-at-login
- Local web dashboard for setup, status and manual runs

**Out of scope**

- Editing map name/description/tags after creation. Users hand-edit
  `geoguessr.json`.
- Installing or updating vali itself, or downloading vali country data.
  GeoVali only *detects* a missing `vali` and tells the user the command.
- Browsing a picker of all the user's existing GeoGuessr maps. Deferred; see
  section 13.
- Location-lake maintenance, Discord/Sheets/R2 integration, monitoring,
  subdivision auto-generation, and every other concern of the author's larger
  private pipeline.

## 3. Stack

**.NET 10, single ASP.NET Core project, distributed as a .NET global tool.**

Package `GeoVali`, command `geovali`, installed with
`dotnet tool install -g GeoVali`.

Rationale:

- vali is itself a .NET global tool and multi-targets `net8.0;net10.0`, so a
  user who installs the .NET 10 SDK can install both vali and GeoVali with no
  `--allow-roll-forward` and no second runtime. One prerequisite covers both,
  and `dotnet tool install -g` is a command these users have already run once
  to get vali.
- Cross-platform (Windows, macOS, Linux) with no per-OS UI work, because the
  interface is a local web page.
- The delicate logic — cadence arithmetic, the minimum-location guard, and the
  four-call GeoGuessr publish sequence with its version increment — already
  exists in C# in the author's pipeline and ports directly rather than being
  re-derived.

Rejected: Node/TypeScript (adds a runtime the audience has no reason to have,
zero code reuse) and Go (a third language to maintain, and its single-binary
advantage is moot when the .NET SDK is a vali prerequisite anyway).

If a zero-SDK audience ever matters, the same codebase can be published as
self-contained single-file binaries per OS. No rewrite, only a different build
target.

## 4. On-disk layout

A **map is a folder**. GeoVali recursively scans the configured root and
treats any directory containing `map.json` as a map. This is the author's
existing convention, so existing `map-definitions` trees work unchanged.

| File | Written by | Source-controlled |
|---|---|---|
| `map.json` | user — vali map definition | yes |
| `geoguessr.json` | GeoVali when the map is set up, hand-edited after | yes |
| `geoguessr.ephemeral.json` | GeoVali every run | no |
| `map-locations.json` | `vali generate` | no |

`vali generate --file <dir>/map.json` writes `<dir>/map-locations.json`
alongside the definition (`outFolder = Path.GetDirectoryName(definitionPath)`),
which is why the folder-as-map convention needs no configuration.

State that matters is plain JSON beside the map, readable and diffable, not
hidden in a database.

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

- `id` — absent until the map is first published, or filled immediately when linking
  an existing map. Written back once, then left alone.
- `published` — when `false`, GeoVali updates the draft but never calls the
  publish endpoint, leaving the map unpublished.
- `updateFrequencyDays` — null means "use the global default".
- `avatar` — generated randomly on creation if absent, then persisted so it is
  stable across runs.
- `description` — supports the `{{LocationCount}}` token, replaced at publish
  time with the actual location count. The token, not the substituted text, is
  what stays in the file.

Fields from the author's pipeline that are deliberately dropped as
slashP-specific: `mapDistributionLink`, `highlighted`,
`shouldIncludeExtremities`.

### `geoguessr.ephemeral.json`

```json
{
  "lastPublishedTimeUtc": "2026-09-04T22:11:04Z",
  "updateCount": 37,
  "lastRunUtc": "2026-09-06T03:00:12Z",
  "lastError": null
}
```

Regenerable state, separate from the committed metadata so it can be
gitignored. GeoVali writes a `.gitignore` entry hint in the docs, but never
modifies the user's `.gitignore`.

## 5. Configuration and credentials

Application config lives outside the maps folder, in the platform location:

- Windows — `%APPDATA%\GeoVali\`
- macOS — `~/Library/Application Support/GeoVali/`
- Linux — `$XDG_CONFIG_HOME/geovali/`, falling back to `~/.config/geovali/`

`config.json` holds the maps root, default cadence (7 days), scheduler check
interval (30 minutes), dashboard port (5099, next free port if taken), and the
start-at-login flag.

`credentials.json` holds only the `_ncfa` cookie value, stored separately from
config so it is never printed in a diagnostics dump. At rest it is DPAPI-
protected (`CurrentUser` scope) on Windows and mode `0600` on macOS and Linux.

This asymmetry is intentional and documented to the user: on Unix the
protection is file permissions, not encryption. The alternative — an OS
keychain — costs a native dependency per platform, which is not worth it for a
session cookie the user can revoke by logging out of GeoGuessr.

The cookie is never logged, never included in error messages, and never sent
anywhere except `https://www.geoguessr.com`.

## 6. Components

One process. ASP.NET Core Minimal API serving an embedded static page, plus a
`BackgroundService` timer.

| Component | Responsibility |
|---|---|
| `MapScanner` | Recursively find map folders under the root; classify each as configured (`geoguessr.json` present) or not |
| `Cadence` | Pure due/not-due arithmetic |
| `ValiRunner` | Invoke `vali generate`, stream stdout, surface exit code. Behind an interface |
| `GeoguessrClient` | Auth probe, draft create, draft read, draft update, publish. Behind an interface |
| `UpdateRunner` | Orchestrate one run: preflight, then per map generate → publish → stamp |
| `Scheduler` | `BackgroundService` timer that invokes `UpdateRunner` |
| `Autostart` | Register/deregister start-at-login per OS |
| `ConfigStore` / `CredentialStore` | Read/write config and the protected cookie |
| `RunLog` | Rolling log file plus an in-memory ring buffer for the dashboard |
| Web API + `wwwroot` | Dashboard endpoints and the embedded UI |

Each unit is independently testable: `Cadence` is pure, `ValiRunner` and
`GeoguessrClient` are interfaces, and `UpdateRunner` depends only on those
interfaces plus the filesystem.

## 7. The run loop

Runs are **serial, one map at a time**. vali generation is CPU- and
network-heavy; serial execution keeps resource use predictable and progress
legible. A single run-lock means a manual "Run now" during an active run joins
the in-flight run rather than starting a second.

### Preflight, before any map is touched

1. `vali` resolvable on `PATH`. If not, abort with the exact
   `dotnet tool install -g vali` command.
2. Cookie valid — one `GET /api/v3/profiles`. If it fails with `401`, abort the
   entire run and raise the re-authenticate banner.

Checking auth *before* generation is the most important ordering decision in
the tool. Generation across a folder can take tens of minutes, and a stale
cookie is the most likely failure. Discovering it after regenerating every map
must be structurally impossible.

### Per map

1. `vali generate --file <dir>/map.json`; stdout streamed to the dashboard.
   Non-zero exit fails this map and moves to the next.
2. Read `map-locations.json`. Fewer than 5 locations fails the map with an
   explicit message rather than letting GeoGuessr answer a bare `400`.
3. Publish (section 8).
4. On success, write `id` back to `geoguessr.json` if it changed, then stamp
   `geoguessr.ephemeral.json` with `lastPublishedTimeUtc = UtcNow` and an
   incremented `updateCount`.

### Cadence

Whole **local calendar days** elapsed since `lastPublishedTimeUtc`, ported from
the author's `MapCadence.IsDue`. "Every 1 day" therefore means "if it has not
already been done today", independent of how long the previous run took.

Resolution order is per-map `updateFrequencyDays`, then the global default. The
author's folder-name special cases are dropped; they encode one person's tree.

## 8. GeoGuessr publishing

Base address `https://www.geoguessr.com/`, every request carrying
`Cookie: _ncfa=<value>`.

1. If `geoguessr.json` has no `id`:
   `POST /api/v4/user-maps/drafts` with `{ "name": ..., "mode": "coordinates" }`
   and take `id` from the response.
2. `GET /api/v4/user-maps/drafts/{id}` and read `version`.
3. `PUT /api/v4/user-maps/drafts/{id}` with the full map body at
   `version + 1`, `customCoordinates` mapped from `map-locations.json`
   (`lat`, `lng`, `heading`, `pitch`, `panoId` when non-empty).
4. If `published` is true, `PUT /api/v4/user-maps/drafts/{id}/publish` with an
   empty body.

The read-then-write-incremented-version step is mandatory; the API rejects a
stale version, and getting it wrong fails silently in ways that are hard to
diagnose. It is covered by an explicit test (section 11).

### Linking an existing map

The user pastes a GeoGuessr map URL. GeoVali extracts the id and issues
`GET /api/v4/user-maps/drafts/{id}`; success proves the signed-in user owns the
draft and yields the current name, which seeds `geoguessr.json`. This reuses a
call already in the publish path and costs nothing extra.

## 9. Error handling

**Stamp only on success.** A map that generates but fails to publish is never
stamped, so it stays due and is redone end to end on the next run. There are no
half-updated states, no resume logic and no reconciliation. The cost is
regenerating on retry, which is the right trade for a tool that must be
debuggable by non-developers.

- **Per-map failures are isolated.** One bad map never stops the others. The
  message is recorded in that map's `lastError` and shown inline in the
  dashboard.
- **`401` aborts the whole run.** An expired cookie is not a per-map failure and
  must not be recorded as forty of them.
- **Transient network errors** get up to 3 retries with exponential backoff, on
  the publish calls only. Generation is never auto-retried within a run.
- **Logs.** A rolling file in the config directory holds detail; the dashboard
  shows the recent in-memory buffer. Neither ever contains the cookie.

## 10. User interface

Static HTML with vanilla JavaScript, served from resources embedded in the
assembly, with Server-Sent Events for live progress. **No frontend build step**
— the global tool stays a single package with no npm involved, and the page can
be opened and read directly. Adding a build step later, if the dashboard
outgrows this, is a contained change.

### First run

Two screens.

1. **Choose the maps folder.** A browser cannot open a native directory dialog
   for a server, but the server is local, so GeoVali renders its own folder
   browser: click through directories, see which contain `map.json`, confirm.
   This avoids asking a non-developer to type an absolute path and gives
   immediate feedback that the chosen folder actually holds maps.
2. **Paste the `_ncfa` cookie.** The screen shows the walkthrough — F12 →
   Application → Cookies → `geoguessr.com` → `_ncfa` → copy value — beside the
   paste box, and validates on submit via `GET /api/v3/profiles`, confirming
   with the signed-in username.

### Dashboard

Header shows the maps folder, auth status and the next scheduled run. A table
lists each map with name, cadence, last published, status and a per-row "Run
now", above "Run all due" and "Run everything". A live panel shows the current
map and a tail of vali's stdout during a run.

A folder with `map.json` but no `geoguessr.json` appears in a "not set up yet"
state offering **Create new map** — prompts for name and description, generates
an avatar, writes `geoguessr.json` — or **Link existing** (section 8).

Settings covers default cadence, check interval, start-at-login, re-pasting the
cookie and changing the maps folder.

## 11. Testing

Tests target what actually breaks. `ValiRunner` and `GeoguessrClient` sit
behind interfaces, so no test shells out to real vali or touches the network.

- `GeoguessrClient` against a stubbed `HttpMessageHandler`, asserting the exact
  four-call sequence and that the PUT carries `version + 1`. This quirk is
  silent when wrong and is the most likely thing to regress.
- `Cadence` as pure table tests, including the local-calendar-day boundary and
  the per-map-override resolution order.
- The minimum-location guard: 4 locations must fail before any HTTP call is
  made.
- An end-to-end run against a **fake vali executable** — a script that writes a
  canned `map-locations.json` — and a stub HTTP server, covering preflight
  abort on `401`, per-map failure isolation, and stamp-only-on-success.
- `MapScanner` over temporary directory trees.

Implementation follows TDD: each behaviour above gets its failing test first.

## 12. Distribution

`dotnet pack` produces the tool package; `dotnet tool install -g GeoVali`
installs it. Running `geovali` starts the server, opens the default browser at
the dashboard, and runs until quit.

Start-at-login is a settings toggle implemented per OS: Startup-folder shortcut
on Windows, `LaunchAgent` plist on macOS, systemd user unit on Linux.

## 13. Deferred

- **Browse-and-pick from the user's existing GeoGuessr maps.**
  `GET /api/v3/profiles/maps` exists and is auth-gated, making it the natural
  candidate, but its response shape is unverified. The paste-a-URL flow covers
  the same need at no cost, so the picker waits until the basic tool is proven.
- Editing map metadata from the dashboard after creation.
- Self-contained per-OS binaries for users without the .NET SDK.
