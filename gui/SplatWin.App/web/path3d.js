// SPLAT! for Windows, stage G8: the 3D path view. It draws what the app computed (PathScene, SPLAT!'s
// own arithmetic) and nothing more: heights are SPLAT!'s "normalized" heights, with Earth's curvature
// taken out, so the line of sight is straight from one antenna top to the other. Height exaggeration
// scales every height about the same base, so what is above or below the line of sight stays so.
import * as THREE from "three";
import { OrbitControls } from "./vendor/three-0.180.0/OrbitControls.js";

const FT = 5280;          // feet per mile
const U = 1 / 100;        // scene units per foot (keeps numbers small for the GPU)
const $ = (id) => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const post = (m) => { if (host) host.postMessage(m); };

let S = null;                                      // the scene from the app
let exag = 3, view = "rx", ground = "topo", fresMode = "fz";
let base = 0, z = null, latOff = null, lonOff = null;
let renderer, scene, camera, controls, terrainMesh, dynamic, labels = [], masts = {};
let mapCache = {};                                 // ground kind -> { canvas, z, x0, y0 } for the current strip
let mapState = "none";

try { const v = JSON.parse(localStorage.getItem("splat3d") || "{}"); if (v.exag) exag = v.exag; if (v.ground) ground = v.ground; if (v.fres) fresMode = v.fres; } catch { }
function save() { try { localStorage.setItem("splat3d", JSON.stringify({ exag, ground, fres: fresMode })); } catch { } }

// ---------- units ----------
const metric = () => S && S.metric;
const dist = (mi) => metric() ? `${(mi * 1.609344).toFixed(2)} km` : `${mi.toFixed(2)} mi`;
const len = (ft) => metric() ? `${Math.round(ft * 0.3048)} m` : `${Math.round(ft)} ft`;

// ---------- setup ----------
function init() {
  renderer = new THREE.WebGLRenderer({ antialias: true, logarithmicDepthBuffer: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  $("view").appendChild(renderer.domElement);
  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x9fc3e6);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x445544, 1.6));
  const sun = new THREE.DirectionalLight(0xffffff, 1.6);
  sun.position.set(-0.4, 1, -0.6);
  scene.add(sun);
  camera = new THREE.PerspectiveCamera(35, 1, 0.02, 1e5);
  controls = new OrbitControls(camera, renderer.domElement);
  controls.addEventListener("change", render);
  new ResizeObserver(resize).observe($("view"));
  renderer.domElement.addEventListener("pointerdown", (e) => { downAt = [e.clientX, e.clientY]; });
  renderer.domElement.addEventListener("pointerup", pick);

  $("ex").value = exag; $("exv").textContent = `${exag}×`; $("ground").value = ground;
  $("ex").addEventListener("input", () => { exag = +$("ex").value; $("exv").textContent = `${exag}×`; save(); rebuild(true); report(); });
  $("ground").addEventListener("change", () => { ground = $("ground").value; save(); applyGround(); });
  $("fres").addEventListener("change", () => { fresMode = $("fres").value; save(); rebuild(true); });
  $("width").addEventListener("change", () => { busy("Reading the terrain…"); post({ type: "width", miles: +$("width").value }); });
  document.querySelectorAll("[data-view]").forEach((b) => b.addEventListener("click", () => setView(b.dataset.view)));
  $("reset").addEventListener("click", () => setView(view));
  $("help").addEventListener("click", () => post({ type: "help" }));
  window.addEventListener("keydown", (e) => {
    if (e.target.tagName === "SELECT" || e.target.tagName === "INPUT") return;
    const k = { "1": "rx", "2": "tx", "3": "side", "4": "over" }[e.key];
    if (k) setView(k); else if (e.key === "r" || e.key === "R") setView(view); else if (e.key === "F1") { e.preventDefault(); post({ type: "help" }); }
    else if (e.key === "+" || e.key === "=" || e.key === "-") {
      exag = Math.min(20, Math.max(1, exag + (e.key === "-" ? -0.5 : 0.5)));
      $("ex").value = exag; $("exv").textContent = `${exag}×`; save(); rebuild(true); report();
    }
  });
}

function resize() {
  const w = $("view").clientWidth, h = $("view").clientHeight;
  renderer.setSize(w, h, false);
  renderer.domElement.style.width = "100%"; renderer.domElement.style.height = "100%";
  camera.aspect = w / Math.max(1, h);
  camera.updateProjectionMatrix();
  render();
}

function busy(text, error) {
  // Before the first drawing it fills the window; after that it's a small note, so the view stays visible.
  const b = $("busy");
  b.textContent = text; b.style.display = text ? "" : "none";
  b.className = (error ? "error" : "") + (S && !error ? " note" : "");
}

// ---------- the scene from the app ----------
function f32(b64) { const s = atob(b64), u = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i); return new Float32Array(u.buffer); }

function load(sc) {
  S = sc;
  z = f32(S.z); latOff = f32(S.latOff); lonOff = f32(S.lonOff);
  mapCache = {};
  let lo = Infinity;
  for (const v of z) if (!Number.isNaN(v) && v < lo) lo = v;
  for (const v of S.profile.z) if (v < lo) lo = v;
  base = lo - 20;
  // Choices that depend on the scene
  const w = $("width"); w.innerHTML = "";
  for (const mi of [0.25, 0.5, 1, 2, 5]) {
    const o = document.createElement("option"); o.value = mi;
    o.textContent = metric() ? `${(mi * 1.609344).toFixed(mi < 1 ? 1 : 0)} km each side` : `${mi} mi each side`;
    if (Math.abs(mi - S.halfWidthMiles) < 1e-9) o.selected = true;
    w.appendChild(o);
  }
  const pct = Math.round(S.fresnelClearance * 1000) / 10;
  const f = $("fres"); f.innerHTML = "";
  for (const [v, t] of [["fz", `${pct} % zone`], ["full", "Full first zone"], ["off", "Off"]]) {
    const o = document.createElement("option"); o.value = v; o.textContent = t; if (v === fresMode) o.selected = true; f.appendChild(o);
  }
  $("v-rx").textContent = `From ${S.rx.name}`; $("v-tx").textContent = `From ${S.tx.name}`;
  $("summary").textContent = S.summary;
  $("summary").className = S.obstructions.length ? "blocked" : "";
  $("notes").textContent = S.notes.join(" ");
  $("info").hidden = false;
  busy("");
  rebuild(false);
  setView(view);
  applyGround();
}

// scene coordinates: X along the path (from RX), Y up, Z to the right looking from RX to TX
const X = (mi) => mi * FT * U;
const Y = (ft) => (ft - base) * exag * U;
const Zc = (mi) => mi * FT * U;
const losAt = (mi) => S.rx.top + (S.tx.top - S.rx.top) * (mi / S.lengthMiles);

function rebuild(keepCamera) {
  if (!S) return;
  if (terrainMesh) { scene.remove(terrainMesh); terrainMesh.geometry.dispose(); }
  if (dynamic) { scene.remove(dynamic); dynamic.traverse((o) => o.geometry && o.geometry.dispose()); }
  labels.forEach((l) => l.el.remove()); labels = [];
  const nr = S.rows.length, nc = S.cross.length;

  // terrain
  const pos = new Float32Array(nr * nc * 3), col = new Float32Array(nr * nc * 3);
  let zmin = Infinity, zmax = -Infinity;
  for (const v of z) if (!Number.isNaN(v)) { zmin = Math.min(zmin, v); zmax = Math.max(zmax, v); }
  const c = new THREE.Color();
  for (let r = 0; r < nr; r++) for (let k = 0; k < nc; k++) {
    const i = r * nc + k, v = z[i];
    pos[3 * i] = X(S.rows[r]); pos[3 * i + 1] = Number.isNaN(v) ? 0 : Y(v); pos[3 * i + 2] = Zc(S.cross[k]);
    heightColor(Number.isNaN(v) ? zmin : (v - zmin) / Math.max(1, zmax - zmin), c);
    col[3 * i] = c.r; col[3 * i + 1] = c.g; col[3 * i + 2] = c.b;
  }
  const idx = [];
  for (let r = 0; r < nr - 1; r++) for (let k = 0; k < nc - 1; k++) {
    const a = r * nc + k, b = a + 1, d = a + nc, e = d + 1;
    if (Number.isNaN(z[a]) || Number.isNaN(z[b]) || Number.isNaN(z[d]) || Number.isNaN(z[e])) continue;
    idx.push(a, d, b, b, d, e);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
  g.setAttribute("color", new THREE.BufferAttribute(col, 3));
  g.setIndex(new THREE.BufferAttribute(new Uint32Array(idx), 1));
  g.computeVertexNormals();
  terrainMesh = new THREE.Mesh(g, new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: 2, polygonOffsetUnits: 2 }));
  scene.add(terrainMesh);
  if (ground !== "heights" && mapCache[ground]) dressMap(mapCache[ground]);

  dynamic = new THREE.Group();
  scene.add(dynamic);
  const L = S.lengthMiles, size = Math.max(L * FT * U, 1);
  const thick = Math.max(size * 0.0006, 0.04);
  const rxTop = new THREE.Vector3(X(0), Y(S.rx.top), 0), txTop = new THREE.Vector3(X(L), Y(S.tx.top), 0);

  // line of sight
  dynamic.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.LineCurve3(rxTop, txTop), 1, thick, 8), new THREE.MeshBasicMaterial({ color: 0x00e5ff })));

  // Fresnel zone: an ellipse at each row (heights exaggerated like everything else)
  if (fresMode !== "off") {
    const frac = fresMode === "full" ? 1 : S.fresnelClearance, seg = 28;
    const fp = [], fi = [];
    for (let r = 0; r < nr; r++) {
      const mi = S.rows[r], rad = S.fresnel[r] * frac, cy = Y(losAt(mi));
      for (let s = 0; s < seg; s++) {
        const a = (s / seg) * Math.PI * 2;
        fp.push(X(mi), cy + Math.sin(a) * rad * exag * U, Math.cos(a) * rad * U);
      }
    }
    for (let r = 0; r < nr - 1; r++) for (let s = 0; s < seg; s++) {
      const a = r * seg + s, b = r * seg + (s + 1) % seg;
      fi.push(a, a + seg, b, b, a + seg, b + seg);
    }
    const fg = new THREE.BufferGeometry();
    fg.setAttribute("position", new THREE.Float32BufferAttribute(fp, 3));
    fg.setIndex(fi);
    dynamic.add(new THREE.Mesh(fg, new THREE.MeshBasicMaterial({ color: 0x5aa0ff, transparent: true, opacity: 0.14, side: THREE.DoubleSide, depthWrite: false })));
    // Its lower edge, where terrain matters: a line along the bottom of the zone.
    const ep = [];
    for (let r = 0; r < nr; r++) { const mi = S.rows[r]; ep.push(X(mi), Y(losAt(mi)) - S.fresnel[r] * frac * exag * U, 0); }
    const eg = new THREE.BufferGeometry(); eg.setAttribute("position", new THREE.Float32BufferAttribute(ep, 3));
    dynamic.add(new THREE.Line(eg, new THREE.LineBasicMaterial({ color: 0x2f6fff })));
    $("lg-fres").textContent = fresMode === "full" ? "First Fresnel zone" : `${Math.round(S.fresnelClearance * 1000) / 10} % Fresnel zone`;
  }

  // the ground right under the path: yellow, red where it rises above the line of sight
  const pp = [], pc = [], yel = new THREE.Color(0xffd23f), red = new THREE.Color(0xff4d4d);
  const P = S.profile;
  for (let i = 0; i < P.s.length; i++) {
    pp.push(X(P.s[i]), Y(P.z[i]) + thick * 0.5, 0);
    const above = i > 0 && i < P.s.length - 1 && P.h[i] > 0;
    const cc = above ? red : yel; pc.push(cc.r, cc.g, cc.b);
  }
  const pg = new THREE.BufferGeometry();
  pg.setAttribute("position", new THREE.Float32BufferAttribute(pp, 3));
  pg.setAttribute("color", new THREE.Float32BufferAttribute(pc, 3));
  dynamic.add(new THREE.Line(pg, new THREE.LineBasicMaterial({ vertexColors: true })));

  // masts
  const mastMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
  for (const [site, at, key] of [[S.rx, 0, "rx"], [S.tx, L, "tx"]]) {
    const yb = Y(site.ground), yt = Y(site.top), h = Math.max(yt - yb, 1e-3);
    const m = new THREE.Mesh(new THREE.CylinderGeometry(thick * 1.3, thick * 1.3, h, 8), mastMat);
    m.position.set(X(at), yb + h / 2, 0); dynamic.add(m);
    masts[key] = { mesh: m, label: label(`${site.name} · ${len(site.alt)} mast`, new THREE.Vector3(X(at), yt, 0), "") };
  }

  // Each antenna top as a target: solid where it can be seen, and a faint outline that shows through
  // hills, so from the other antenna you see at once whether the terrain hides it.
  // (Markers are a fixed size on screen, whatever their distance, so a near one never hides what it marks.)
  for (const [site, at, key] of [[S.rx, 0, "rx"], [S.tx, L, "tx"]]) {
    const p = new THREE.Vector3(X(at), Y(site.top), 0);
    const ghost = marker(p, 0xffffff, 22, "ring", false);
    const solid = marker(p, 0xffe14d, 16, "dot", true);
    masts[key].targets = [solid, ghost];
  }

  // SPLAT!'s obstruction points
  for (const o of S.obstructions) marker(new THREE.Vector3(X(o.s), Y(o.z), 0), 0xff3030, 11, "dot", true);
  if (S.obstructions.length) {
    const o = S.obstructions.reduce((a, b) => (b.amsl > a.amsl ? b : a));
    label(`In the way: ${dist(o.s)} from ${S.rx.name}`, new THREE.Vector3(X(o.s), Y(o.z), 0), "obs");
  }

  selfCheck();
  hideOwnMast();
  render();
}

// Standing on an antenna, its own mast would fill the view: hide it (and its label) from there.
function hideOwnMast() {
  for (const k of ["rx", "tx"]) if (masts[k]) {
    masts[k].mesh.visible = view !== k; masts[k].label.hidden = view === k;
    if (masts[k].targets) masts[k].targets.forEach((t) => (t.visible = view !== k));
  }
}

const sprites = {};
function spriteTexture(kind) {
  if (sprites[kind]) return sprites[kind];
  const c = document.createElement("canvas"); c.width = c.height = 64;
  const g = c.getContext("2d");
  g.beginPath(); g.arc(32, 32, kind === "ring" ? 26 : 28, 0, Math.PI * 2);
  if (kind === "ring") { g.lineWidth = 7; g.strokeStyle = "#fff"; g.stroke(); }
  else { g.fillStyle = "#fff"; g.fill(); g.lineWidth = 5; g.strokeStyle = "rgba(0,0,0,.55)"; g.stroke(); }
  return (sprites[kind] = new THREE.CanvasTexture(c));
}
// A marker drawn the same size on screen at any distance. "depth": hidden by terrain in front of it.
function marker(at, color, px, kind, depth) {
  const g = new THREE.BufferGeometry(); g.setAttribute("position", new THREE.Float32BufferAttribute([at.x, at.y, at.z], 3));
  const m = new THREE.Points(g, new THREE.PointsMaterial({ color, size: px * window.devicePixelRatio, sizeAttenuation: false, map: spriteTexture(kind),
                                                           transparent: true, alphaTest: 0.3, depthTest: depth, depthWrite: depth }));
  if (!depth) m.renderOrder = 10;
  dynamic.add(m);
  return m;
}

function heightColor(t, out) {
  // green lowlands to brown hills to pale tops
  const stops = [[0, [0.27, 0.55, 0.3]], [0.45, [0.62, 0.66, 0.38]], [0.75, [0.6, 0.47, 0.32]], [1, [0.93, 0.91, 0.88]]];
  t = Math.min(1, Math.max(0, t));
  for (let i = 1; i < stops.length; i++) if (t <= stops[i][0]) {
    const [t0, a] = stops[i - 1], [t1, b] = stops[i], f = (t - t0) / (t1 - t0);
    return out.setRGB(a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f);
  }
  return out.setRGB(...stops[stops.length - 1][1]);
}

function label(text, at, cls) {
  const el = document.createElement("div");
  el.className = "label " + cls; el.textContent = text;
  $("labels").appendChild(el);
  const l = { el, at, hidden: false };
  labels.push(l);
  return l;
}

// ---------- viewpoints ----------
function setView(v) {
  if (!S) return;
  view = v;
  document.querySelectorAll("[data-view]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.view === v)));
  const L = S.lengthMiles, size = L * FT * U;
  const rx = new THREE.Vector3(X(0), Y(S.rx.top), 0), tx = new THREE.Vector3(X(L), Y(S.tx.top), 0);
  const mid = new THREE.Vector3(X(L / 2), Y(losAt(L / 2)), 0);
  camera.fov = 35;
  if (v === "rx") { camera.position.copy(rx); controls.target.copy(tx); camera.fov = 30; }
  else if (v === "tx") { camera.position.copy(tx); controls.target.copy(rx); camera.fov = 30; }
  else if (v === "side") {
    // Fit the whole path across the window.
    const fit = (size / 2) * 1.2 / (Math.tan((camera.fov * Math.PI) / 360) * Math.max(camera.aspect, 0.3));
    camera.position.set(mid.x, mid.y, Math.max(fit, Zc(S.halfWidthMiles) * 2.5)); controls.target.copy(mid);
  }
  else { camera.position.set(X(-L * 0.35), mid.y + size * 0.45, Zc(-L * 0.45)); controls.target.copy(mid); }
  // Standing exactly on the antenna, step back a hair so the orbit target isn't the camera.
  if (v === "rx" || v === "tx") camera.position.add(new THREE.Vector3().subVectors(camera.position, controls.target).setLength(Math.max(size * 0.002, 0.05)));
  camera.near = Math.max(size * 1e-5, 0.005); camera.far = size * 20 + 1000;
  camera.updateProjectionMatrix();
  controls.update();
  hideOwnMast();
  render();
  report();
}

// ---------- the ground: map tiles or height colors ----------
const SOURCES = {
  topo: { url: (z, x, y) => `https://${"abc"[(x + y) % 3]}.tile.opentopomap.org/${z}/${x}/${y}.png`, max: 16,
          attr: 'Map data © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors, SRTM | Map style © <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)' },
  streets: { url: (z, x, y) => `https://tile.openstreetmap.org/${z}/${x}/${y}.png`, max: 17,
             attr: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' },
};
const mx = (lon, zm) => ((lon + 180) / 360) * 256 * 2 ** zm;
const my = (lat, zm) => { const r = (lat * Math.PI) / 180; return ((1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2) * 256 * 2 ** zm; };

async function applyGround() {
  if (!S) return;
  $("attrib").innerHTML = ground === "heights" ? "Terrain: SPLAT!'s elevation data" : SOURCES[ground].attr;
  if (ground === "heights") { dressHeights(); mapState = "heights"; report(); return; }
  if (!mapCache[ground]) {
    const want = ground, src = SOURCES[want], bb = S.bbox;
    let zm = src.max;
    while (zm > 3 && (mx(bb.east, zm) - mx(bb.west, zm) > 4096 || my(bb.south, zm) - my(bb.north, zm) > 4096)) zm--;
    const x0 = Math.floor(mx(bb.west, zm) / 256), x1 = Math.floor(mx(bb.east, zm) / 256);
    const y0 = Math.floor(my(bb.north, zm) / 256), y1 = Math.floor(my(bb.south, zm) / 256);
    const canvas = document.createElement("canvas");
    canvas.width = (x1 - x0 + 1) * 256; canvas.height = (y1 - y0 + 1) * 256;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "#d9d4c7"; ctx.fillRect(0, 0, canvas.width, canvas.height);
    busy("Loading the map for the ground…");
    let ok = 0, failed = 0;
    const jobs = [];
    for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) jobs.push(new Promise((res) => {
      const img = new Image(); img.crossOrigin = "anonymous";
      img.onload = () => { ctx.drawImage(img, (x - x0) * 256, (y - y0) * 256); ok++; res(); };
      img.onerror = () => { failed++; res(); };
      img.src = src.url(zm, x, y);
    }));
    await Promise.all(jobs);
    busy("");
    if (want !== ground || !S) return;
    if (ok === 0) {
      mapState = "failed";
      dressHeights();
      $("notes").textContent = [...S.notes, "The map for the ground couldn't be loaded (no internet?), so the ground is colored by height."].join(" ");
      report();
      return;
    }
    mapCache[ground] = { canvas, zm, x0: x0 * 256, y0: y0 * 256, failed };
  }
  dressMap(mapCache[ground]);
  mapState = ground + (mapCache[ground].failed ? ` (${mapCache[ground].failed} tiles missing)` : "");
  report();
}

function dressMap(m) {
  const nr = S.rows.length, nc = S.cross.length, uv = new Float32Array(nr * nc * 2);
  for (let i = 0; i < nr * nc; i++) {
    const lat = S.lat0 + latOff[i], lon = S.lon0 + lonOff[i];
    uv[2 * i] = (mx(lon, m.zm) - m.x0) / m.canvas.width;
    uv[2 * i + 1] = 1 - (my(lat, m.zm) - m.y0) / m.canvas.height;
  }
  terrainMesh.geometry.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
  if (!m.texture) {
    m.texture = new THREE.CanvasTexture(m.canvas);
    m.texture.colorSpace = THREE.SRGBColorSpace;
    m.texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
  }
  terrainMesh.material.dispose();
  terrainMesh.material = new THREE.MeshLambertMaterial({ map: m.texture, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: 2, polygonOffsetUnits: 2 });
  render();
}

function dressHeights() {
  terrainMesh.material.dispose();
  terrainMesh.material = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: 2, polygonOffsetUnits: 2 });
  render();
}

// ---------- reading the ground ----------
let downAt = null;
const ray = new THREE.Raycaster();
function pick(e) {
  if (!S || !downAt || Math.hypot(e.clientX - downAt[0], e.clientY - downAt[1]) > 4) return;   // a drag, not a click
  const r = renderer.domElement.getBoundingClientRect();
  ray.setFromCamera(new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1), camera);
  const hit = ray.intersectObject(terrainMesh)[0];
  if (!hit) return;
  const mi = hit.point.x / U / FT, side = hit.point.z / U / FT;
  const ft = hit.point.y / U / exag + base;
  const d = ft - losAt(Math.min(Math.max(mi, 0), S.lengthMiles));
  const where = Math.abs(side) < 0.01 ? "on the path" : `${dist(Math.abs(side))} ${side > 0 ? "right" : "left"} of the path`;
  $("readout").textContent = `${dist(mi)} from ${S.rx.name}, ${where}: the ground is ${len(Math.abs(d))} ${d >= 0 ? "ABOVE" : "below"} the line of sight's height there.`;
}

// ---------- self-check and report ----------
// At every exaggeration the page offers (1x to 20x), each point along the path is on the same side of the line of sight as at 1x.
let selfcheck = "not run";
function selfCheck() {
  const P = S.profile;
  for (const k of [1, 1.5, 2, 3, 5, 7.5, 10, 15, 20]) for (let i = 1; i < P.s.length - 1; i++) {
    const g = (P.z[i] - base) * k, l = (losAt(P.s[i]) - base) * k;
    if (Math.sign(g - l) !== Math.sign(P.z[i] - losAt(P.s[i])) && Math.abs(P.h[i]) > 1e-6) { selfcheck = `FAILED at ${P.s[i]} mi, ${k}x`; return; }
    if (Math.abs((P.z[i] - losAt(P.s[i])) - P.h[i]) > 1e-6 * Math.max(1, Math.abs(P.h[i])) + 1e-6) { selfcheck = `FAILED: height above line of sight at ${P.s[i]} mi`; return; }
  }
  selfcheck = "ok";
}

function report() {
  if (!S) return;
  post({ type: "shown", text: `3D view: ${S.rx.name} to ${S.tx.name}; rows=${S.rows.length}; cols=${S.cross.length}; obstructions=${S.obstructions.length}; ` +
                              `view=${view}; ground=${mapState}; exaggeration=${exag}; fresnel=${fresMode}; width=${S.halfWidthMiles}; selfcheck=${selfcheck}; summary=${S.summary}` });
}

function render() {
  if (!renderer || !S) return;
  renderer.render(scene, camera);
  const w = renderer.domElement.clientWidth, h = renderer.domElement.clientHeight, v = new THREE.Vector3();
  for (const l of labels) {
    v.copy(l.at).project(camera);
    const vis = !l.hidden && v.z < 1 && v.z > -1 && Math.abs(v.x) < 1.1 && Math.abs(v.y) < 1.1;
    l.el.style.display = vis ? "" : "none";
    if (vis) { l.el.style.left = `${((v.x + 1) / 2) * w}px`; l.el.style.top = `${((1 - v.y) / 2) * h}px`; }
  }
}

// ---------- messages from the app ----------
function onMessage(m) {
  if (m.type === "scene") load(m.scene);
  else if (m.type === "busy") busy(m.text);
  else if (m.type === "error") { busy(m.text, true); post({ type: "shown", text: "Problem: " + m.text }); }
}

init();
if (host) { host.addEventListener("message", (e) => onMessage(e.data)); post({ type: "ready" }); }
else {
  // Outside the app (for checking in a browser): path3d.html?scene=<file.json>
  const f = new URLSearchParams(location.search).get("scene");
  if (f) fetch(f).then((r) => r.json()).then((sc) => onMessage({ type: "scene", scene: sc })).catch((e) => busy(String(e), true));
  else busy("This page is the 3D view inside SPLAT! for Windows.");
}
