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
