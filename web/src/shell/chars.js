/**
 * The cast, and how to draw it.  [shell agent owns this file]
 *
 * 2D canvas portraits for menus (`drawPortrait`) plus a thin binding onto the
 * real `chars/` rig (`charMesh`/`charBeat`/`disposeChar`) for the 3D podium.
 * `chars/index.js` is a hard dependency, not an optional one — this file no
 * longer probes for it or falls back to a stand-in shape when it's absent.
 */

import * as THREE from 'three';
import { PAL, num } from './theme.js';
import { makeCast, BUILD_IDS } from '../chars/index.js';

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

// ------------------------------------------------------------------ 3D cast

/** Which rig build (silhouette) each 2D character design reads closest to. */
const BUILD_BY_SHAPE = {
  round: 'round', beak: 'round', blob: 'round',
  tall: 'tall',
  tiny: 'small', star: 'small',
  spike: 'wide', block: 'wide',
};

/**
 * Build a single 3D character via `chars/`'s real roster facade (`makeCast`)
 * rather than a bespoke one-off construction path, so the shell's characters
 * run through the same rig/animator every minigame's cast does.
 * @returns {THREE.Group}
 */
export function charMesh(def, opts = {}) {
  const idx = Math.max(0, CHARS.findIndex((c) => c.id === def.id));
  const build = BUILD_BY_SHAPE[def.shape] || BUILD_IDS[idx % BUILD_IDS.length];
  const cast = makeCast({
    count: 1,
    positions: [[0, 0, 0]],
    builds: [build],
    players: [{ id: def.id, name: def.name, palette: paletteForChar(def) }],
    ...opts,
  });
  const member = cast.get(0);
  const obj = member.char;
  addCrest(obj, def);
  obj.userData.charApi = member.anim;
  obj.userData.def = def;
  // Kept so disposeChar() can tear down this single-member cast; makeCast's
  // own dispose() only frees this member's geometry/materials, not the
  // shared caches other live characters still use.
  obj.userData.castHandle = cast;
  return obj;
}

// ------------------------------------------------- identity: palette + crest

const palCache = new Map();
const _c = new THREE.Color();

/**
 * The rig palette for a 2D character design, derived from its own colour and
 * accent so the 3D figure and the menu portrait are the same character —
 * bright body, darker trim for value contrast, a pale head, dark eyes.
 */
export function paletteForChar(def) {
  let p = palCache.get(def.id);
  if (p) return p;
  const mix = (a, b, t) => _c.setHex(a).lerp(new THREE.Color(b), t).getHex();
  p = {
    id: `char:${def.id}`, name: def.name,
    body: def.color,
    limb: mix(def.color, 0xffffff, 0.18),
    trim: mix(def.color, 0x100818, 0.62),
    skin: mix(def.color, 0xffffff, 0.8),
    accent: def.accent,
    eye: mix(def.color, 0x06030c, 0.88),
    rim: mix(def.color, 0xffffff, 0.35),
  };
  palCache.set(def.id, p);
  return p;
}

const crestMats = new Map();
function crestMat(hex, side = THREE.FrontSide) {
  const key = `${hex}:${side}`;
  let m = crestMats.get(key);
  if (!m) { m = new THREE.MeshStandardMaterial({ color: hex, roughness: 0.4, side }); crestMats.set(key, m); }
  return m;
}
const crestGeos = new Map();
function cgeo(key, make) {
  let g = crestGeos.get(key);
  if (!g) { g = make(); crestGeos.set(key, g); }
  return g;
}

/**
 * Give the 3D figure its portrait's crest (antenna, bolt, plume, horns, cap,
 * fin). Crests that should wobble hang off the rig's `bobble` joint, which
 * the animator already drives with a lagging spring — so a plume or a fin
 * keeps swinging a beat after the body stops, for free. The crest replaces
 * the build's generic head gear so the silhouettes stay distinct.
 */
function addCrest(char, def) {
  const j = char.joints;
  const b = char.build;
  const headTop = b.head.h * 0.5;
  const hw = b.head.w;
  const acc = crestMat(def.accent);
  const dark = crestMat(_c.setHex(def.color).lerp(new THREE.Color(0x100818), 0.55).getHex());
  if (j.gear && j.gear.parent === j.head) j.gear.visible = false;
  const keepBall = def.crest === 'antenna';
  if (j.bobbleMesh && !keepBall) j.bobbleMesh.visible = false;
  const add = (parent, geo, mat, x, y, z, rx = 0, ry = 0, rz = 0) => {
    const m = new THREE.Mesh(geo, mat);
    m.position.set(x, y, z);
    m.rotation.set(rx, ry, rz);
    m.castShadow = true;
    parent.add(m);
    return m;
  };
  // The bobble joint sits `bobbleY` above the head centre; offsets below are
  // in its frame, so y = headTop - bobbleY is the scalp.
  const scalp = headTop - b.bobbleY;
  switch (def.crest) {
    case 'antenna': {
      const L = Math.max(0.04, -scalp);
      add(j.bobble, cgeo(`ant:${b.id}`, () => new THREE.CylinderGeometry(0.016, 0.02, L, 6)), dark, 0, -L / 2, 0);
      break;
    }
    case 'bolt': {
      const g = cgeo(`bolt:${b.id}`, () => {
        const s = new THREE.Shape();
        const h = hw * 0.75;
        s.moveTo(0, 0); s.lineTo(h * 0.18, h * 0.5); s.lineTo(h * 0.02, h * 0.5);
        s.lineTo(h * 0.22, h); s.lineTo(-h * 0.2, h * 0.38); s.lineTo(-h * 0.02, h * 0.38); s.lineTo(-h * 0.14, 0);
        const e = new THREE.ExtrudeGeometry(s, { depth: 0.05, bevelEnabled: false });
        e.translate(0, 0, -0.025);
        return e;
      });
      add(j.bobble, g, acc, 0, scalp - 0.02, 0);
      break;
    }
    case 'plume': {
      const g = cgeo(`plume:${b.id}`, () => new THREE.SphereGeometry(hw * 0.13, 10, 8).scale(0.55, 2.4, 0.55));
      for (const [ang, s] of [[-0.45, 0.85], [0, 1], [0.45, 0.85]]) {
        const m = add(j.bobble, g, acc, Math.sin(ang) * hw * 0.12, scalp + hw * 0.24 * s, -hw * 0.05, -0.35, 0, ang);
        m.scale.setScalar(s);
      }
      break;
    }
    case 'horns': {
      const g = cgeo(`horn:${b.id}`, () => new THREE.ConeGeometry(hw * 0.1, hw * 0.34, 10));
      for (const s of [-1, 1]) add(j.head, g, crestMat(0xfff1d6), s * hw * 0.34, headTop + hw * 0.08, 0, 0, 0, -s * 0.55);
      break;
    }
    case 'cap': {
      const dome = cgeo(`capd:${b.id}`, () => new THREE.SphereGeometry(hw * 0.52, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2));
      const brim = cgeo(`capb:${b.id}`, () => new THREE.CylinderGeometry(hw * 0.42, hw * 0.42, 0.025, 18, 1, false, -Math.PI / 2, Math.PI));
      add(j.head, dome, acc, 0, headTop - hw * 0.18, 0);
      add(j.head, brim, acc, 0, headTop - hw * 0.16, hw * 0.28);
      break;
    }
    case 'fin': {
      const g = cgeo(`fin:${b.id}`, () => {
        const c = new THREE.CircleGeometry(hw * 0.36, 16, 0, Math.PI);
        c.rotateY(Math.PI / 2);
        return c;
      });
      add(j.bobble, g, crestMat(def.accent, THREE.DoubleSide), 0, scalp - 0.01, 0);
      break;
    }
    default: break;
  }
}

/**
 * A gold crown on the character's head (party leader / winner). Returns the
 * mesh group; `crown.visible` toggles it. Geometry is shared.
 */
export function addCrown(char) {
  const j = char.joints;
  const b = char.build;
  const hw = b.head.w;
  const gold = crestMat(0xffd23d);
  const band = cgeo('crownBand', () => new THREE.CylinderGeometry(0.5, 0.46, 0.22, 20, 1, true));
  const spike = cgeo('crownSpike', () => new THREE.ConeGeometry(0.09, 0.26, 8));
  const gem = cgeo('crownGem', () => new THREE.SphereGeometry(0.06, 10, 8));
  const g = new THREE.Group();
  const bandMesh = new THREE.Mesh(band, crestMat(0xffd23d, THREE.DoubleSide));
  g.add(bandMesh);
  for (let i = 0; i < 5; i++) {
    const a = (i / 5) * Math.PI * 2;
    const s = new THREE.Mesh(spike, gold);
    s.position.set(Math.sin(a) * 0.47, 0.22, Math.cos(a) * 0.47);
    g.add(s);
    const d = new THREE.Mesh(gem, crestMat(i % 2 ? 0xff4d8a : 0x39d4ff));
    d.position.set(Math.sin(a) * 0.5, 0.02, Math.cos(a) * 0.5);
    g.add(d);
  }
  g.scale.setScalar(hw * 0.62);
  g.position.set(0, b.head.h * 0.5 + hw * 0.05, 0);
  g.rotation.x = -0.12;
  j.head.add(g);
  return g;
}

/** Per-frame idle/dance, driven by the real beat-phase animator. */
export function charBeat(obj, beat, dt) {
  obj?.userData?.charApi?.update(dt, beat);
}

/** Tear down a character built by `charMesh()`. */
export function disposeChar(obj) {
  const cast = obj?.userData?.castHandle;
  if (cast) { try { cast.dispose(); } catch { /* ignore */ } }
}

// ------------------------------------------------------------- portraits

const BUST = 256;
let bustCache = null;

/**
 * Head-and-shoulders renders of every character, made once on a throwaway
 * WebGL context (so the main renderer's post chain and state are untouched)
 * and kept as 2D canvases. Empty when WebGL is unavailable — drawPortrait
 * then falls back to the 2D drawing.
 */
function busts() {
  if (bustCache) return bustCache;
  bustCache = new Map();
  let r = null;
  try {
    const cv = document.createElement('canvas');
    cv.width = cv.height = BUST;
    r = new THREE.WebGLRenderer({ canvas: cv, antialias: true, alpha: true, preserveDrawingBuffer: true });
    r.setPixelRatio(1);
    r.setSize(BUST, BUST, false);
    r.setClearColor(0x000000, 0);
    const scene = new THREE.Scene();
    scene.add(new THREE.HemisphereLight(0xdfe6ff, 0x2a1d5e, 1.6));
    const key = new THREE.DirectionalLight(0xffffff, 2.2);
    key.position.set(2, 3, 4);
    scene.add(key);
    const rim = new THREE.DirectionalLight(0x9fd0ff, 1.2);
    rim.position.set(-3, 2, -2);
    scene.add(rim);
    const cam = new THREE.PerspectiveCamera(28, 1, 0.05, 50);
    const box = new THREE.Box3();
    for (const def of CHARS) {
      const m = charMesh(def, {});
      scene.add(m);
      m.rotation.y = -0.32;
      const api = m.userData.charApi;
      for (let i = 0; i < 20; i++) api?.update(1 / 30, 0.5 + i / 30);
      m.updateMatrixWorld(true);
      box.setFromObject(m);
      const h = box.max.y - box.min.y;
      const ty = box.min.y + h * 0.66;
      const span = h * 0.78;
      const dist = span / 2 / Math.tan(THREE.MathUtils.degToRad(14));
      cam.position.set(0.18 * h, ty + h * 0.06, dist);
      cam.lookAt(0, ty, 0);
      r.render(scene, cam);
      const out = document.createElement('canvas');
      out.width = out.height = BUST;
      out.getContext('2d').drawImage(cv, 0, 0);
      bustCache.set(def.id, out);
      scene.remove(m);
      disposeChar(m);
    }
  } catch (e) {
    console.warn('portrait busts unavailable', e);
    bustCache.clear();
  } finally {
    // dispose() only: forceContextLoss() makes three log "Context Lost".
    if (r) r.dispose();
  }
  return bustCache;
}

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

  // The portrait IS the 3D character: a bust rendered once from the real rig
  // (the hand-drawn smileys and hexagons didn't resemble the models at all).
  const b = busts().get(def.id);
  if (b) {
    g.drawImage(b, 0, 0, S, S);
    return canvas;
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
