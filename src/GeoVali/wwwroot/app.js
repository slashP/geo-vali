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
