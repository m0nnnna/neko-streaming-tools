const TOKEN_KEY = "nekostreamer-remote-token";
let token = localStorage.getItem(TOKEN_KEY) || "";

const el = (id) => document.getElementById(id);

// Scanning the pairing QR code opens this page with ?token=... — grab it, persist it,
// and scrub it from the visible URL/history so it doesn't linger in a screenshot.
const urlToken = new URLSearchParams(location.search).get("token");
if (urlToken) {
  token = urlToken;
  localStorage.setItem(TOKEN_KEY, token);
  history.replaceState(null, "", location.pathname);
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

function highlightScene(name) {
  document.querySelectorAll("#scenes button").forEach((b) => {
    b.classList.toggle("active", b.dataset.name === name);
  });
}

async function refreshStatus() {
  try {
    const res = await api("/api/status");
    if (!res.ok) return;
    const s = await res.json();
    setDot("obsDot", s.obsConnected);
    setDot("vtsDot", s.vtsConnected && s.vtsAuthenticated);
    highlightScene(s.currentScene);
  } catch {
    // swallow — surfaced via the status dots already
  }
}

async function loadScenes() {
  let res;
  try {
    res = await api("/api/obs/scenes");
  } catch {
    return;
  }
  if (!res.ok) {
    el("scenes").innerHTML = '<p class="hint">OBS not connected</p>';
    return;
  }
  const { current, scenes } = await res.json();
  el("scenes").innerHTML = "";
  for (const name of scenes) {
    const btn = document.createElement("button");
    btn.textContent = name;
    btn.dataset.name = name;
    if (name === current) btn.classList.add("active");
    btn.addEventListener("click", () => switchScene(name));
    el("scenes").appendChild(btn);
  }
}

async function switchScene(name) {
  highlightScene(name);
  try {
    await api(`/api/obs/scenes/${encodeURIComponent(name)}`, { method: "POST" });
  } catch {
    // ignore — next status poll will reconcile
  }
}

async function loadHotkeys() {
  let res;
  try {
    res = await api("/api/vts/hotkeys");
  } catch {
    return;
  }
  if (!res.ok) {
    el("hotkeys").innerHTML = '<p class="hint">VTube Studio not connected</p>';
    return;
  }
  const hotkeys = await res.json();
  el("hotkeys").innerHTML = "";
  for (const hk of hotkeys) {
    const btn = document.createElement("button");
    btn.textContent = hk.name;
    btn.addEventListener("click", () => triggerHotkey(hk.hotkeyId, btn));
    el("hotkeys").appendChild(btn);
  }
}

async function triggerHotkey(id, btn) {
  btn.classList.add("pressed");
  setTimeout(() => btn.classList.remove("pressed"), 200);
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

refreshAll();
setInterval(refreshStatus, 3000);
setInterval(loadScenes, 15000);
setInterval(loadHotkeys, 15000);
