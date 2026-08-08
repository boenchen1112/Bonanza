/**
 * The cast, and how to draw it.  [shell agent owns this file]
 *
 * The characters agent is building `src/chars/`. Until that lands (and if it
 * ever fails to load) the shell still has to show something with a face on it,
 * so this file carries a complete procedural fallback: 2D canvas portraits for
 * menus and low-poly 3D stand-ins for the podium.
 *
 * The hand-off is a single adapter, `charMesh()`. When `chars/index.js` exists
 * it is picked up automatically by the `import.meta.glob` below — a glob that
 * matches nothing compiles to an empty object rather than a build error, which
 * is what lets this file ship before that module does.
 */

import * as THREE from 'three';
import { PAL, num } from './theme.js';

/** @typedef {{id:string,name:string,color:number,accent:number,trait:string,shape:string,crest:string}} CharDef */

/** @type {CharDef[]} */
export const CHARS = [
  { id: 'bopp', name: 'BOPP', color: num(PAL.yellow), accent: 0xff9f45, trait: 'All rhythm, no brakes.', shape: 'round', crest: 'antenna' },
  { id: 'zizz', name: 'ZIZZ', color: num(PAL.cyan), accent: 0x7aa6ff, trait: 'Runs on static and spite.', shape: 'spike', crest: 'bolt' },
  { id: 'kwark', name: 'KWARK', color: num(PAL.coral), accent: 0xff9f45, trait: 'Beak first, ask later.', shape: 'beak', crest: 'plume' },
  { id: 'tuff', name: 'TUFF', color: num(PAL.green), accent: 0x39d4b4, trait: 'Built like a downbeat.', shape: 'block', crest: 'horns' },
  { id: 'mimo', name: 'MIMO', color: num(PAL.violet), accent: 0xff7ad9, trait: 'Two beats ahead, always.', shape: 'tall', crest: 'cap' },
  { id: 'nibb', name: 'NIBB', color: num(PAL.orange), accent: 0xffd93d, trait: 'Small. Loud. Everywhere.', shape: 'tiny', crest: 'antenna' },
  { id: 'glub', name: 'GLUB', color: num(PAL.teal), accent: 0x4dd6ff, trait: 'Wobbles exactly on time.', shape: 'blob', crest: 'fin' },
  { id: 'fizz', name: 'FIZZ', color: num(PAL.pink), accent: 0xc08cff, trait: 'Sparkles on the offbeat.', shape: 'star', crest: 'plume' },
];

export const charById = (id) => CHARS.find((c) => c.id === id) || CHARS[0];

// --------------------------------------------------------------- adapter

const CHAR_MODULE = import.meta.glob('../chars/index.js');
let charModPromise = null;
let charMod = null;

/** Kick off the optional chars/ load once; never blocks a screen. */
export function preloadChars() {
  if (charModPromise) return charModPromise;
  const key = Object.keys(CHAR_MODULE)[0];
  charModPromise = key
    ? CHAR_MODULE[key]().then((m) => { charMod = m; return m; }).catch(() => null)
    : Promise.resolve(null);
  return charModPromise;
}

/**
 * Build a 3D character. Uses `chars/` when present, the stand-in otherwise.
 * Swapping the real cast in is exactly this function and nothing else.
 * @returns {THREE.Group}
 */
export function charMesh(def, opts = {}) {
  if (charMod) {
    const factory = charMod.createCharacter || charMod.makeCharacter || charMod.create || charMod.default;
    if (typeof factory === 'function') {
      try {
        const made = factory({ id: def.id, color: def.color, accent: def.accent, THREE, ...opts });
        const obj = made?.root || made?.object3D || made;
        if (obj && obj.isObject3D) {
          obj.userData.charApi = made;
          obj.userData.def = def;
          return obj;
        }
      } catch { /* fall through to the stand-in */ }
    }
  }
  return standIn(def, opts);
}

/** Per-frame idle/dance. Delegates if the real character exposes an update. */
export function charBeat(obj, beat, dt, energy = 1) {
  if (!obj) return;
  const api = obj.userData?.charApi;
  if (api && typeof api.update === 'function') {
    try { api.update(dt, beat, energy); return; } catch { /* fall through */ }
  }
  const ph = obj.userData.phase || 0;
  const b = beat + ph;
  const bounce = Math.abs(Math.sin(b * Math.PI));
  const sq = 1 - bounce * 0.14 * energy;
  obj.scale.set(1 + (1 - sq) * 0.5, sq, 1 + (1 - sq) * 0.5);
  obj.position.y = (obj.userData.baseY || 0) + bounce * 0.34 * energy;
  obj.rotation.z = Math.sin(b * Math.PI * 0.5) * 0.10 * energy;
  obj.rotation.y = Math.sin(b * Math.PI * 0.25) * 0.28;
}

// ------------------------------------------------------------- stand-ins

const GEO = {};
function geo(key, make) { return GEO[key] || (GEO[key] = make()); }

/** Low-poly stand-in: a body, a face and a crest. 3 draw calls per character. */
function standIn(def, { scale = 1 } = {}) {
  const g = new THREE.Group();
  const mat = new THREE.MeshStandardMaterial({
    color: def.color, roughness: 0.42, metalness: 0.05, flatShading: true,
  });
  const dark = new THREE.MeshStandardMaterial({ color: 0x1a1733, roughness: 0.8, flatShading: true });
  const accent = new THREE.MeshStandardMaterial({
    color: def.accent, roughness: 0.35, metalness: 0.1, flatShading: true,
  });

  let body;
  switch (def.shape) {
    case 'block':
      body = new THREE.Mesh(geo('box', () => new THREE.BoxGeometry(1.05, 1.0, 0.95)), mat); break;
    case 'tall':
      body = new THREE.Mesh(geo('tall', () => new THREE.CapsuleGeometry(0.42, 0.8, 3, 8)), mat); break;
    case 'spike':
      body = new THREE.Mesh(geo('spike', () => new THREE.OctahedronGeometry(0.72, 0)), mat); break;
    case 'tiny':
      body = new THREE.Mesh(geo('tiny', () => new THREE.IcosahedronGeometry(0.46, 0)), mat); break;
    case 'star':
      body = new THREE.Mesh(geo('star', () => new THREE.DodecahedronGeometry(0.62, 0)), mat); break;
    case 'beak':
      body = new THREE.Mesh(geo('beakb', () => new THREE.SphereGeometry(0.62, 10, 8)), mat); break;
    case 'blob':
      body = new THREE.Mesh(geo('blob', () => new THREE.SphereGeometry(0.66, 8, 6)), mat); break;
    default:
      body = new THREE.Mesh(geo('round', () => new THREE.IcosahedronGeometry(0.66, 1)), mat);
  }
  body.position.y = 0.66;
  g.add(body);

  // face: one dark visor plate reads as eyes at any distance and costs one call
  const visor = new THREE.Mesh(geo('visor', () => new THREE.BoxGeometry(0.62, 0.2, 0.1)), dark);
  visor.position.set(0, 0.78, 0.55);
  g.add(visor);

  let crest;
  if (def.crest === 'antenna') {
    crest = new THREE.Mesh(geo('ant', () => new THREE.ConeGeometry(0.1, 0.5, 6)), accent);
    crest.position.set(0, 1.42, 0);
  } else if (def.crest === 'bolt') {
    crest = new THREE.Mesh(geo('bolt', () => new THREE.TetrahedronGeometry(0.3, 0)), accent);
    crest.position.set(0, 1.4, 0);
  } else if (def.crest === 'horns') {
    crest = new THREE.Mesh(geo('horn', () => new THREE.TorusGeometry(0.34, 0.07, 4, 10, Math.PI)), accent);
    crest.position.set(0, 1.24, 0);
  } else if (def.crest === 'cap') {
    crest = new THREE.Mesh(geo('cap', () => new THREE.CylinderGeometry(0.42, 0.5, 0.22, 8)), accent);
    crest.position.set(0, 1.28, 0);
  } else if (def.crest === 'fin') {
    crest = new THREE.Mesh(geo('fin', () => new THREE.ConeGeometry(0.26, 0.6, 3)), accent);
    crest.position.set(0, 1.32, -0.05);
  } else {
    crest = new THREE.Mesh(geo('plume', () => new THREE.ConeGeometry(0.22, 0.62, 5)), accent);
    crest.position.set(0, 1.36, -0.1);
    crest.rotation.x = -0.35;
  }
  g.add(crest);

  g.scale.setScalar(scale);
  g.userData.def = def;
  g.userData.baseY = 0;
  g.userData.materials = [mat, dark, accent];
  return g;
}

/** Free the stand-in materials. Shared geometry is cached and kept. */
export function disposeChar(obj) {
  if (!obj) return;
  const api = obj.userData?.charApi;
  if (api && typeof api.dispose === 'function') { try { api.dispose(); } catch { /* ignore */ } }
  for (const m of obj.userData?.materials || []) m.dispose?.();
}

// ------------------------------------------------------------- portraits

/**
 * Draw a character portrait into a canvas, procedurally. Deliberately drawn
 * ONCE and then animated with CSS transforms: eight canvases repainting every
 * frame is a menu that costs more than the game.
 */
export function drawPortrait(canvas, def, { size = 128, bg = true } = {}) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = size * dpr;
  canvas.height = size * dpr;
  canvas.style.width = size + 'px';
  canvas.style.height = size + 'px';
  const g = canvas.getContext('2d');
  if (!g) return canvas;
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, size, size);

  const col = '#' + def.color.toString(16).padStart(6, '0');
  const acc = '#' + def.accent.toString(16).padStart(6, '0');
  const S = size;

  if (bg) {
    const grd = g.createLinearGradient(0, 0, 0, S);
    grd.addColorStop(0, shade(col, -0.55));
    grd.addColorStop(1, shade(col, -0.82));
    g.fillStyle = grd;
    roundRect(g, 0, 0, S, S, S * 0.11);
    g.fill();
    // radial glow behind the head
    const rg = g.createRadialGradient(S * 0.5, S * 0.56, S * 0.05, S * 0.5, S * 0.56, S * 0.5);
    rg.addColorStop(0, hexA(col, 0.34));
    rg.addColorStop(1, hexA(col, 0));
    g.fillStyle = rg;
    g.fillRect(0, 0, S, S);
  }

  const cx = S * 0.5;
  const cy = S * 0.58;
  const r = S * 0.28;

  // crest behind the body
  g.save();
  g.fillStyle = acc;
  g.strokeStyle = '#120f2e';
  g.lineWidth = S * 0.035;
  crest(g, def.crest, cx, cy - r, S);
  g.restore();

  // body
  g.save();
  g.fillStyle = col;
  g.strokeStyle = '#120f2e';
  g.lineWidth = S * 0.04;
  bodyPath(g, def.shape, cx, cy, r);
  g.fill();
  g.stroke();
  // top highlight
  g.globalCompositeOperation = 'source-atop';
  const hl = g.createLinearGradient(0, cy - r * 1.2, 0, cy + r);
  hl.addColorStop(0, 'rgba(255,255,255,.45)');
  hl.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = hl;
  g.fillRect(cx - r * 1.6, cy - r * 1.6, r * 3.2, r * 3.2);
  g.restore();

  // eyes
  const ey = cy - r * 0.14;
  const ex = r * 0.36;
  g.fillStyle = '#151230';
  ellipse(g, cx - ex, ey, r * 0.20, r * 0.24);
  ellipse(g, cx + ex, ey, r * 0.20, r * 0.24);
  g.fillStyle = '#fff';
  ellipse(g, cx - ex + r * 0.07, ey - r * 0.08, r * 0.075, r * 0.085);
  ellipse(g, cx + ex + r * 0.07, ey - r * 0.08, r * 0.075, r * 0.085);

  // mouth — one confident curve
  g.strokeStyle = '#151230';
  g.lineWidth = S * 0.028;
  g.lineCap = 'round';
  g.beginPath();
  g.arc(cx, ey + r * 0.28, r * 0.30, 0.22 * Math.PI, 0.78 * Math.PI);
  g.stroke();

  // name plate
  g.fillStyle = 'rgba(10,9,24,.72)';
  roundRect(g, S * 0.08, S * 0.855, S * 0.84, S * 0.11, S * 0.05);
  g.fill();
  g.fillStyle = '#fff';
  g.font = `900 ${Math.round(S * 0.088)}px system-ui, sans-serif`;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(def.name, S * 0.5, S * 0.914);

  return canvas;
}

function bodyPath(g, shape, cx, cy, r) {
  g.beginPath();
  switch (shape) {
    case 'block':
      rr(g, cx - r * 0.92, cy - r * 0.86, r * 1.84, r * 1.72, r * 0.24); break;
    case 'tall':
      rr(g, cx - r * 0.66, cy - r * 1.12, r * 1.32, r * 2.2, r * 0.62); break;
    case 'spike':
      poly(g, cx, cy, r * 1.06, 6, -Math.PI / 2); break;
    case 'star':
      star(g, cx, cy, r * 1.15, r * 0.66, 6); break;
    case 'tiny':
      g.arc(cx, cy + r * 0.12, r * 0.78, 0, Math.PI * 2); break;
    case 'beak':
      g.moveTo(cx + r * 1.28, cy + r * 0.12);
      g.lineTo(cx + r * 0.5, cy - r * 0.16);
      g.lineTo(cx + r * 0.5, cy + r * 0.46);
      g.closePath();
      g.moveTo(cx + r, cy);
      g.arc(cx, cy, r, 0, Math.PI * 2);
      break;
    case 'blob':
      blob(g, cx, cy, r); break;
    default:
      g.arc(cx, cy, r, 0, Math.PI * 2);
  }
}

function crest(g, kind, cx, top, S) {
  g.beginPath();
  switch (kind) {
    case 'antenna':
      g.lineWidth = S * 0.035;
      g.strokeStyle = g.fillStyle;
      g.moveTo(cx, top + S * 0.03);
      g.lineTo(cx, top - S * 0.11);
      g.stroke();
      g.beginPath();
      g.arc(cx, top - S * 0.13, S * 0.038, 0, Math.PI * 2);
      g.fill();
      break;
    case 'bolt':
      g.moveTo(cx - S * 0.05, top + S * 0.02);
      g.lineTo(cx + S * 0.02, top - S * 0.12);
      g.lineTo(cx - S * 0.005, top - S * 0.10);
      g.lineTo(cx + S * 0.06, top - S * 0.02);
      g.lineTo(cx + S * 0.02, top - S * 0.01);
      g.closePath();
      g.fill();
      break;
    case 'horns':
      g.moveTo(cx - S * 0.13, top + S * 0.04);
      g.quadraticCurveTo(cx - S * 0.15, top - S * 0.09, cx - S * 0.05, top - S * 0.02);
      g.moveTo(cx + S * 0.13, top + S * 0.04);
      g.quadraticCurveTo(cx + S * 0.15, top - S * 0.09, cx + S * 0.05, top - S * 0.02);
      g.lineWidth = S * 0.045;
      g.strokeStyle = g.fillStyle;
      g.stroke();
      break;
    case 'cap':
      rr(g, cx - S * 0.15, top - S * 0.055, S * 0.30, S * 0.075, S * 0.03);
      g.fill();
      break;
    case 'fin':
      g.moveTo(cx, top - S * 0.13);
      g.lineTo(cx + S * 0.07, top + S * 0.03);
      g.lineTo(cx - S * 0.07, top + S * 0.03);
      g.closePath();
      g.fill();
      break;
    default: // plume
      for (let i = -1; i <= 1; i++) {
        g.beginPath();
        g.ellipse(cx + i * S * 0.06, top - S * 0.06, S * 0.028, S * 0.075, i * 0.4, 0, Math.PI * 2);
        g.fill();
      }
  }
}

// -- tiny 2D helpers --------------------------------------------------------

function rr(g, x, y, w, h, r) {
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}
function roundRect(g, x, y, w, h, r) { g.beginPath(); rr(g, x, y, w, h, r); }
function ellipse(g, x, y, rx, ry) { g.beginPath(); g.ellipse(x, y, rx, ry, 0, 0, Math.PI * 2); g.fill(); }
function poly(g, cx, cy, r, n, rot) {
  for (let i = 0; i < n; i++) {
    const a = rot + (i / n) * Math.PI * 2;
    const x = cx + Math.cos(a) * r, y = cy + Math.sin(a) * r;
    if (i === 0) g.moveTo(x, y); else g.lineTo(x, y);
  }
  g.closePath();
}
function star(g, cx, cy, R, r, n) {
  for (let i = 0; i < n * 2; i++) {
    const a = -Math.PI / 2 + (i / (n * 2)) * Math.PI * 2;
    const rad = i % 2 ? r : R;
    const x = cx + Math.cos(a) * rad, y = cy + Math.sin(a) * rad;
    if (i === 0) g.moveTo(x, y); else g.lineTo(x, y);
  }
  g.closePath();
}
function blob(g, cx, cy, r) {
  const pts = 7;
  for (let i = 0; i <= pts; i++) {
    const a = (i / pts) * Math.PI * 2;
    const rad = r * (0.86 + 0.16 * Math.sin(i * 2.3));
    const x = cx + Math.cos(a) * rad, y = cy + Math.sin(a) * rad * 0.94;
    if (i === 0) g.moveTo(x, y); else g.lineTo(x, y);
  }
  g.closePath();
}

export function shade(hexStr, amt) {
  const n = parseInt(hexStr.replace('#', ''), 16);
  let r = (n >> 16) & 255, gg = (n >> 8) & 255, b = n & 255;
  if (amt >= 0) { r += (255 - r) * amt; gg += (255 - gg) * amt; b += (255 - b) * amt; }
  else { r *= 1 + amt; gg *= 1 + amt; b *= 1 + amt; }
  return `rgb(${r | 0},${gg | 0},${b | 0})`;
}
export function hexA(hexStr, a) {
  const n = parseInt(hexStr.replace('#', ''), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}
