const TOKEN_KEY = "nekostreamer-remote-token";
const TAB_KEY = "nekostreamer-remote-tab";
const CUSTOM_KEY = "nekostreamer-remote-customize";
const DEMO_KEY = "nekostreamer-remote-demo";
let token = localStorage.getItem(TOKEN_KEY) || "";

const el = (id) => document.getElementById(id);

// Scanning the pairing QR code opens this page with ?token=... — grab it, persist it.
// ?demo=1 / ?demo=0 flips demo mode (fake scenes/hotkeys, no OBS/VTS required) and
// also persists, so it survives a refresh without the query string. Both are scrubbed
// from the visible URL/history in one go so neither lingers in a screenshot.
const urlParams = new URLSearchParams(location.search);
const urlToken = urlParams.get("token");
if (urlToken) {
  token = urlToken;
  localStorage.setItem(TOKEN_KEY, token);
}
if (urlParams.has("demo")) {
  localStorage.setItem(DEMO_KEY, urlParams.get("demo") === "0" ? "" : "1");
}
if (urlToken !== null || urlParams.has("demo")) {
  history.replaceState(null, "", location.pathname);
}
const demoMode = localStorage.getItem(DEMO_KEY) === "1";
el("demoBanner").hidden = !demoMode;

const DEMO_SCENES = { current: "Starting Soon", scenes: ["Starting Soon", "Just Chatting", "In Game", "BRB", "Ending"] };
const DEMO_HOTKEYS = [
  { hotkeyId: "hk1", name: "Wave" },
  { hotkeyId: "hk2", name: "Blush" },
  { hotkeyId: "hk3", name: "Surprised" },
  { hotkeyId: "hk4", name: "Heart Eyes" },
  { hotkeyId: "hk5", name: "Sleepy" },
  { hotkeyId: "hk6", name: "Angry" },
  { hotkeyId: "hk7", name: "Sparkle" },
];

// Per-item customization (rename/hide/reorder) lives only on this phone, same as the
// pairing token — there's no server-side account to hang it off of, and it's inherently
// a per-device preference anyway (two phones may want different layouts).
function emptyCategory() {
  return { order: [], hidden: [], labels: {} };
}
function loadCustom() {
  try {
    const parsed = JSON.parse(localStorage.getItem(CUSTOM_KEY));
    return {
      obs: { ...emptyCategory(), ...(parsed?.obs || {}) },
      vts: { ...emptyCategory(), ...(parsed?.vts || {}) },
    };
  } catch {
    return { obs: emptyCategory(), vts: emptyCategory() };
  }
}
const custom = loadCustom();
function saveCustom() {
  localStorage.setItem(CUSTOM_KEY, JSON.stringify(custom));
}

// Reconciles the live list of ids (scene names / hotkey ids) from the server against the
// stored order: keeps known ids in their saved order, appends new ones at the end, and
// drops ids that no longer exist. Mutates cat.order in place and returns it.
function syncOrder(cat, ids) {
  const idSet = new Set(ids);
  const order = cat.order.filter((id) => idSet.has(id));
  const known = new Set(order);
  for (const id of ids) if (!known.has(id)) order.push(id);
  cat.order = order;
  cat.hidden = cat.hidden.filter((id) => idSet.has(id));
  return order;
}
function toggleHide(cat, id) {
  const i = cat.hidden.indexOf(id);
  if (i === -1) cat.hidden.push(id);
  else cat.hidden.splice(i, 1);
  saveCustom();
}
function setLabel(cat, id, value) {
  if (value) cat.labels[id] = value;
  else delete cat.labels[id];
  saveCustom();
}
function moveItem(order, idx, dir) {
  const j = idx + dir;
  if (j < 0 || j >= order.length) return;
  [order[idx], order[j]] = [order[j], order[idx]];
  saveCustom();
}

async function api(path, opts = {}) {
  const res = await fetch(path, {
    ...opts,
    headers: { ...(opts.headers || {}), "X-Access-Token": token },
  });
  if (res.status === 401) {
    showTokenModal();
    throw new Error("unauthorized");
  }
  return res;
}

function showTokenModal() {
  el("tokenInput").value = token;
  el("tokenModal").hidden = false;
}

el("tokenSave").addEventListener("click", () => {
  token = el("tokenInput").value.trim();
  localStorage.setItem(TOKEN_KEY, token);
  el("tokenModal").hidden = true;
  refreshAll();
});

el("tokenGear").addEventListener("click", showTokenModal);
el("refreshBtn").addEventListener("click", refreshAll);

function setDot(id, ok) {
  el(id).classList.toggle("ok", !!ok);
}

// Only touches the non-edit grid buttons (they carry data-name) — edit-mode rows have
// their own buttons (move/hide) that must never be mistaken for a scene button.
function highlightScene(name) {
  document.querySelectorAll("#scenes button[data-name]").forEach((b) => {
    b.classList.toggle("active", b.dataset.name === name);
  });
}

let activeTab = localStorage.getItem(TAB_KEY) === "vts" ? "vts" : "obs";
function setTab(tab) {
  activeTab = tab;
  localStorage.setItem(TAB_KEY, tab);
  el("tab-obs").classList.toggle("active", tab === "obs");
  el("tab-vts").classList.toggle("active", tab === "vts");
  el("panel-obs").hidden = tab !== "obs";
  el("panel-vts").hidden = tab !== "vts";
}
el("tab-obs").addEventListener("click", () => setTab("obs"));
el("tab-vts").addEventListener("click", () => setTab("vts"));

const editMode = { obs: false, vts: false };
function setEditMode(which, on) {
  editMode[which] = on;
  el(which === "obs" ? "editObs" : "editVts").classList.toggle("editing", on);
  el(which === "obs" ? "editObs" : "editVts").textContent = on ? "Done" : "Edit";
  if (which === "obs") renderScenes();
  else renderHotkeys();
}
el("editObs").addEventListener("click", () => setEditMode("obs", !editMode.obs));
el("editVts").addEventListener("click", () => setEditMode("vts", !editMode.vts));

function buildEditRow({ label, hidden, idx, total, onMove, onLabel, onToggleHide }) {
  const row = document.createElement("div");
  row.className = "edit-row" + (hidden ? " row-hidden" : "");

  const moveWrap = document.createElement("div");
  moveWrap.className = "move-buttons";
  const up = document.createElement("button");
  up.type = "button";
  up.textContent = "▲";
  up.setAttribute("aria-label", "Move up");
  up.disabled = idx === 0;
  up.addEventListener("click", () => onMove(-1));
  const down = document.createElement("button");
  down.type = "button";
  down.textContent = "▼";
  down.setAttribute("aria-label", "Move down");
  down.disabled = idx === total - 1;
  down.addEventListener("click", () => onMove(1));
  moveWrap.append(up, down);

  const input = document.createElement("input");
  input.type = "text";
  input.value = label;
  input.addEventListener("change", () => onLabel(input.value.trim()));

  const hideBtn = document.createElement("button");
  hideBtn.type = "button";
  hideBtn.className = "hide-toggle";
  hideBtn.textContent = hidden ? "Show" : "Hide";
  hideBtn.addEventListener("click", onToggleHide);

  row.append(moveWrap, input, hideBtn);
  return row;
}

let lastScenes = null;
function renderScenes() {
  const container = el("scenes");
  container.classList.toggle("edit-mode", editMode.obs);
  if (!lastScenes || lastScenes.scenes.length === 0) {
    container.innerHTML = '<p class="hint">No scenes</p>';
    return;
  }
  const order = syncOrder(custom.obs, lastScenes.scenes);
  saveCustom();
  container.innerHTML = "";
  let shown = 0;
  order.forEach((name, idx) => {
    const hidden = custom.obs.hidden.includes(name);
    if (!editMode.obs && hidden) return;
    shown++;
    const label = custom.obs.labels[name] || name;
    if (editMode.obs) {
      container.appendChild(buildEditRow({
        label,
        hidden,
        idx,
        total: order.length,
        onMove: (dir) => { moveItem(order, idx, dir); renderScenes(); },
        onLabel: (val) => setLabel(custom.obs, name, val),
        onToggleHide: () => { toggleHide(custom.obs, name); renderScenes(); },
      }));
    } else {
      const btn = document.createElement("button");
      btn.textContent = label;
      btn.dataset.name = name;
      if (name === lastScenes.current) btn.classList.add("active");
      btn.addEventListener("click", () => switchScene(name));
      container.appendChild(btn);
    }
  });
  if (!editMode.obs && shown === 0) {
    container.innerHTML = '<p class="hint">All scenes hidden — tap Edit to show them</p>';
  }
}

let lastHotkeys = null;
function renderHotkeys() {
  const container = el("hotkeys");
  container.classList.toggle("edit-mode", editMode.vts);
  if (!lastHotkeys || lastHotkeys.length === 0) {
    container.innerHTML = '<p class="hint">No hotkeys</p>';
    return;
  }
  const byId = new Map(lastHotkeys.map((hk) => [hk.hotkeyId, hk]));
  const order = syncOrder(custom.vts, lastHotkeys.map((hk) => hk.hotkeyId));
  saveCustom();
  container.innerHTML = "";
  let shown = 0;
  order.forEach((id, idx) => {
    const hidden = custom.vts.hidden.includes(id);
    if (!editMode.vts && hidden) return;
    shown++;
    const hk = byId.get(id);
    const label = custom.vts.labels[id] || hk.name;
    if (editMode.vts) {
      container.appendChild(buildEditRow({
        label,
        hidden,
        idx,
        total: order.length,
        onMove: (dir) => { moveItem(order, idx, dir); renderHotkeys(); },
        onLabel: (val) => setLabel(custom.vts, id, val),
        onToggleHide: () => { toggleHide(custom.vts, id); renderHotkeys(); },
      }));
    } else {
      const btn = document.createElement("button");
      btn.textContent = label;
      btn.addEventListener("click", () => triggerHotkey(id, btn));
      container.appendChild(btn);
    }
  });
  if (!editMode.vts && shown === 0) {
    container.innerHTML = '<p class="hint">All hotkeys hidden — tap Edit to show them</p>';
  }
}

async function refreshStatus() {
  if (demoMode) {
    setDot("obsDot", true);
    setDot("vtsDot", true);
    if (lastScenes) highlightScene(lastScenes.current);
    return;
  }
  try {
    const res = await api("/api/status");
    if (!res.ok) return;
    const s = await res.json();
    setDot("obsDot", s.obsConnected);
    setDot("vtsDot", s.vtsConnected && s.vtsAuthenticated);
    if (lastScenes) lastScenes.current = s.currentScene;
    highlightScene(s.currentScene);
  } catch {
    // swallow — surfaced via the status dots already
  }
}

async function loadScenes() {
  if (demoMode) {
    lastScenes = { current: lastScenes?.current || DEMO_SCENES.current, scenes: DEMO_SCENES.scenes };
    renderScenes();
    return;
  }
  let res;
  try {
    res = await api("/api/obs/scenes");
  } catch {
    return;
  }
  if (!res.ok) {
    lastScenes = null;
    el("scenes").innerHTML = '<p class="hint">OBS not connected</p>';
    return;
  }
  lastScenes = await res.json();
  renderScenes();
}

async function switchScene(name) {
  highlightScene(name);
  if (lastScenes) lastScenes.current = name;
  if (demoMode) return;
  try {
    await api(`/api/obs/scenes/${encodeURIComponent(name)}`, { method: "POST" });
  } catch {
    // ignore — next status poll will reconcile
  }
}

async function loadHotkeys() {
  if (demoMode) {
    lastHotkeys = DEMO_HOTKEYS;
    renderHotkeys();
    return;
  }
  let res;
  try {
    res = await api("/api/vts/hotkeys");
  } catch {
    return;
  }
  if (!res.ok) {
    lastHotkeys = null;
    el("hotkeys").innerHTML = '<p class="hint">VTube Studio not connected</p>';
    return;
  }
  lastHotkeys = await res.json();
  renderHotkeys();
}

async function triggerHotkey(id, btn) {
  btn.classList.add("pressed");
  setTimeout(() => btn.classList.remove("pressed"), 200);
  if (demoMode) return;
  try {
    await api(`/api/vts/hotkeys/${encodeURIComponent(id)}`, { method: "POST" });
  } catch {
    // ignore
  }
}

async function refreshAll() {
  // No upfront check on `token` here — the server itself only cares if pairing is
  // enabled (RequireAccessToken). Try the calls; a 401 from api() shows the modal.
  await refreshStatus();
  await loadScenes();
  await loadHotkeys();
}

setTab(activeTab);
refreshAll();
setInterval(refreshStatus, 3000);
// Skip auto-refresh while a section is being edited — rebuilding the DOM every few
// seconds would blow away an in-progress rename (lost keystrokes/focus).
setInterval(() => { if (!editMode.obs) loadScenes(); }, 15000);
setInterval(() => { if (!editMode.vts) loadHotkeys(); }, 15000);
