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
running until you quit it with Ctrl+C. The dashboard is at `http://127.0.0.1:5099`; if that port is
taken GeoVali walks upwards until it finds a free one and prints the address it settled on. Use
`geovali --no-browser` to start it without opening a browser.

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
  "description": "{{LocationCount}} coastal locations, regenerated every 10 days.",
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

GeoVali only regenerates maps while it is running, so on a machine you use every day it is worth
letting it start by itself. Settings has a **Start at login** toggle. Turning it on registers GeoVali
with whatever your operating system uses for this — nothing is installed system-wide, nothing needs
admin rights, and turning the toggle off undoes it.

What the toggle does, and what is left for you to do:

**Windows** — registers a Scheduled Task named `GeoVali` that starts it when you sign in. Nothing
else to do and nothing to keep open: the task runs GeoVali without an interactive window, so you
never see it unless you go looking. GeoVali runs `schtasks` for you when you flip the toggle — you
never type it — and turning the toggle off deletes the task again. If you do want to look, it is in
Task Scheduler under Task Scheduler Library, named GeoVali.

Two caveats. The task is registered with `/np`, meaning no stored password: GeoVali still reaches
the internet fine, but it cannot reach a network share that needs authentication, so this is the
wrong setup if your maps folder lives on a UNC path. And on a machine whose policy refuses to
register the task, GeoVali falls back to a shortcut in your Startup folder and says so in Settings —
that fallback does show a console window you have to leave open. If you want GeoVali up before
anyone signs in, change the task's trigger to At startup by hand; depending on the machine that
asks for a stored password, which is why GeoVali does not do it for you.

**macOS** — writes `~/Library/LaunchAgents/com.geovali.agent.plist`. Nothing else to do: GeoVali
starts at your next login. To start it now without logging out and back in:

```bash
launchctl load ~/Library/LaunchAgents/com.geovali.agent.plist
```

launchd runs it in the background: no window, no Terminal, nothing to keep open. Its console output
is discarded, so the dashboard and `logs/` are the record of what each run did.

**Linux** — writes `~/.config/systemd/user/geovali.service`, and that is *all* it does. The unit sits
there inert until you run this once:

```bash
systemctl --user enable --now geovali
```

After that GeoVali starts with your session, in the background with no terminal attached, and is
restarted if it crashes. Its console output — the startup banner and web-server messages, not the
run detail — goes to the journal: `journalctl --user -u geovali -f`. What each run actually did is in
the dashboard and in `logs/`. To stop it again, run
`systemctl --user disable --now geovali` — flipping the toggle off deletes the unit file but does not
stop a service that is already running. On a machine you rarely log into, such as a home server,
`loginctl enable-linger $USER` keeps it running while nobody is logged in.

**Anything else** — not supported. The toggle stays off and Settings tells you so.

However it is started, GeoVali runs with `--no-browser`, so nothing pops up in your face at login.
When you do want the dashboard, run `geovali`: it notices the copy already running and opens that
one in your browser instead of starting a second scheduler beside it.

```console
$ geovali
GeoVali is already running — opening http://127.0.0.1:5099
```

## What it deliberately does not do

- Install or update vali, or download vali's country data. It only tells you the command.
- Edit map names, descriptions or tags after setup. Edit `geoguessr.json` by hand.
- Browse your existing GeoGuessr maps. Paste a map's link to connect it to a folder.
