/**
 * The cast, and how to draw it.  [shell agent owns this file]
 *
 * 2D canvas portraits for menus (`drawPortrait`) plus a thin binding onto the
 * real `chars/` rig (`charMesh`/`charBeat`/`disposeChar`) for the 3D podium.
 * `chars/index.js` is a hard dependency, not an optional one — this file no
 * longer probes for it or falls back to a stand-in shape when it's absent.
 */

import { PAL, num } from './theme.js';
import { makeCast, paletteFor, BUILD_IDS } from '../chars/index.js';

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
    players: [{ id: def.id, name: def.name, palette: paletteFor(idx) }],
    ...opts,
  });
  const member = cast.get(0);
  const obj = member.char;
  obj.userData.charApi = member.anim;
  obj.userData.def = def;
  // Kept so disposeChar() can tear down this single-member cast; makeCast's
  // own dispose() only frees this member's geometry/materials, not the
  // shared caches other live characters still use.
  obj.userData.castHandle = cast;
  return obj;
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
