/**
 * Procedural VFX textures.  [render agent owns this directory]
 *
 * Everything here is drawn to a <canvas> at boot. No fetches, ever — the game
 * must run from file:// offline (ARCHITECTURE.md rule 3).
 *
 * Two textures for the whole VFX system:
 *
 *  1. SPRITE ATLAS (512x512, 4x4 cells of 128px). Every particle family bakes
 *     its cell's UV rect into its own geometry at construction, so all
 *     families share one texture and one texture bind. Mipmaps are OFF: at low
 *     LOD a mip chain bleeds neighbouring cells into each other, and the
 *     sparkle aliasing you get instead actually reads as scintillation.
 *
 *  2. WORD ATLAS (1024x512). The verdict/combo vocabulary is fixed and known
 *     at boot, so it is rendered once into rows and drawn as instanced quads
 *     with a per-instance UV rect. That keeps in-world callouts at one draw
 *     call with zero texture uploads during play.
 *
 * Word-atlas colour trick: the outline is drawn in BLACK and the fill in
 * WHITE, both fully opaque. The shader multiplies rgb by the instance tint, so
 * the fill tints and the outline stays black — one texture, any colour, and
 * the callout stays legible against a bright background.
 */

import * as THREE from 'three';
import { makeRng } from '../../core/util.js';

const SIZE = 512;
const GRID = 4;
const CELL = SIZE / GRID; // 128
const PAD = 6; // guard band so bilinear taps never sample the neighbour

/** Cell index per sprite family. */
export const SPRITE = {
  glow: 0,      // soft round falloff — cores, ambient motes
  sparkle: 1,   // 4-point star — the workhorse spark
  streak: 2,    // soft ellipse stretched on X — speed lines, velocity shards
  star6: 3,     // 6-point star — the "big" hit sprite
  scorch: 4,    // irregular blob — ground decals
  confetti: 5,  // flat rounded quad with a sheen band — tumbling confetti
  shard: 6,     // elongated diamond, hot tip — directional impact debris
  smoke: 7,     // puffy soft cloud — the whiff puff
  ringSoft: 8,  // annulus — spare / soft halo
  chip: 9,      // tiny soft square — dust
};

/** UV rect [u0, v0, du, dv] for a sprite cell, accounting for CanvasTexture flipY. */
export function spriteRect(cell) {
  const cx = cell % GRID;
  const cy = Math.floor(cell / GRID);
  const u0 = (cx * CELL + PAD) / SIZE;
  const u1 = ((cx + 1) * CELL - PAD) / SIZE;
  // canvas Y grows downward, UV V grows upward, and CanvasTexture flips Y.
  const v0 = 1 - ((cy + 1) * CELL - PAD) / SIZE;
  const v1 = 1 - (cy * CELL + PAD) / SIZE;
  return [u0, v0, u1 - u0, v1 - v0];
}

/** Rewrite a PlaneGeometry's UVs so it samples one atlas cell. */
export function bakeUv(geo, rect) {
  const uv = geo.attributes.uv;
  for (let i = 0; i < uv.count; i++) {
    uv.setXY(i, rect[0] + uv.getX(i) * rect[2], rect[1] + uv.getY(i) * rect[3]);
  }
  uv.needsUpdate = true;
  return geo;
}

// ---------------------------------------------------------------- drawing

function radial(g, cx, cy, r, stops) {
  const grd = g.createRadialGradient(cx, cy, 0, cx, cy, r);
  for (const [t, c] of stops) grd.addColorStop(t, c);
  return grd;
}

/** Tapered spike from the centre outward, as a filled triangle pair. */
function spike(g, len, halfWidth, angle, alpha) {
  g.save();
  g.rotate(angle);
  const grd = g.createLinearGradient(0, 0, len, 0);
  grd.addColorStop(0, `rgba(255,255,255,${alpha})`);
  grd.addColorStop(0.35, `rgba(255,255,255,${alpha * 0.5})`);
  grd.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grd;
  g.beginPath();
  g.moveTo(0, -halfWidth);
  g.lineTo(len, 0);
  g.lineTo(0, halfWidth);
  g.closePath();
  g.fill();
  g.restore();
}

function drawGlow(g, S) {
  const c = S / 2;
  g.fillStyle = radial(g, c, c, c, [
    [0, 'rgba(255,255,255,1)'],
    [0.18, 'rgba(255,255,255,0.92)'],
    [0.45, 'rgba(255,255,255,0.34)'],
    [0.75, 'rgba(255,255,255,0.07)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function drawSparkle(g, S) {
  const c = S / 2;
  g.save();
  g.translate(c, c);
  // long axis-aligned spikes, short diagonals — reads as a twinkle, not a plus
  for (let i = 0; i < 4; i++) spike(g, c * 0.98, S * 0.045, (i * Math.PI) / 2, 0.95);
  for (let i = 0; i < 4; i++) spike(g, c * 0.44, S * 0.03, Math.PI / 4 + (i * Math.PI) / 2, 0.55);
  g.restore();
  g.fillStyle = radial(g, c, c, c * 0.30, [
    [0, 'rgba(255,255,255,1)'],
    [0.5, 'rgba(255,255,255,0.75)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function drawStreak(g, S) {
  const c = S / 2;
  g.save();
  g.translate(c, c);
  g.scale(1, 0.17); // squash a radial glow into a soft lozenge
  g.fillStyle = radial(g, 0, 0, c, [
    [0, 'rgba(255,255,255,1)'],
    [0.35, 'rgba(255,255,255,0.7)'],
    [0.72, 'rgba(255,255,255,0.16)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(-c, -c, S, S);
  g.restore();
  // hot centre so the streak has a core, not just a smear
  g.save();
  g.translate(c, c);
  g.scale(1, 0.05);
  g.fillStyle = radial(g, 0, 0, c * 0.8, [
    [0, 'rgba(255,255,255,1)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(-c, -c, S, S);
  g.restore();
}

function drawStar6(g, S) {
  const c = S / 2;
  g.save();
  g.translate(c, c);
  for (let i = 0; i < 6; i++) spike(g, c * 0.97, S * 0.055, (i * Math.PI) / 3, 0.9);
  g.restore();
  g.fillStyle = radial(g, c, c, c * 0.42, [
    [0, 'rgba(255,255,255,1)'],
    [0.42, 'rgba(255,255,255,0.8)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function drawScorch(g, S, rng) {
  const c = S / 2;
  // several jittered soft blobs -> an irregular mark that doesn't read as a circle
  for (let i = 0; i < 9; i++) {
    const a = rng() * Math.PI * 2;
    const d = rng() * c * 0.30;
    const r = c * (0.34 + rng() * 0.30);
    g.fillStyle = radial(g, c + Math.cos(a) * d, c + Math.sin(a) * d, r, [
      [0, 'rgba(255,255,255,0.34)'],
      [0.55, 'rgba(255,255,255,0.14)'],
      [1, 'rgba(255,255,255,0)'],
    ]);
    g.fillRect(0, 0, S, S);
  }
  // hot centre
  g.fillStyle = radial(g, c, c, c * 0.34, [
    [0, 'rgba(255,255,255,0.9)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function drawConfetti(g, S) {
  const m = S * 0.12;
  const w = S - m * 2;
  const h = S * 0.62;
  const y = (S - h) / 2;
  g.fillStyle = '#ffffff';
  roundRect(g, m, y, w, h, S * 0.07);
  g.fill();
  // sheen band: darker rgb, still opaque, so a tumbling face catches "light"
  g.globalCompositeOperation = 'source-atop';
  const grd = g.createLinearGradient(m, y, m + w, y + h);
  grd.addColorStop(0, 'rgba(255,255,255,0)');
  grd.addColorStop(0.45, 'rgba(90,90,90,0.85)');
  grd.addColorStop(0.62, 'rgba(255,255,255,0)');
  g.fillStyle = grd;
  g.fillRect(0, 0, S, S);
  g.globalCompositeOperation = 'source-over';
}

function drawShard(g, S) {
  const c = S / 2;
  g.save();
  g.translate(c, c);
  const grd = g.createLinearGradient(-c, 0, c, 0);
  grd.addColorStop(0, 'rgba(255,255,255,0)');
  grd.addColorStop(0.30, 'rgba(255,255,255,0.55)');
  grd.addColorStop(0.72, 'rgba(255,255,255,1)');
  grd.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grd;
  g.beginPath();
  g.moveTo(-c * 0.98, 0);
  g.lineTo(0, -S * 0.16);
  g.lineTo(c * 0.98, 0);
  g.lineTo(0, S * 0.16);
  g.closePath();
  g.fill();
  g.restore();
}

function drawSmoke(g, S, rng) {
  const c = S / 2;
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * Math.PI * 2 + rng();
    const d = c * (0.10 + rng() * 0.22);
    const r = c * (0.42 + rng() * 0.22);
    g.fillStyle = radial(g, c + Math.cos(a) * d, c + Math.sin(a) * d, r, [
      [0, 'rgba(255,255,255,0.32)'],
      [0.6, 'rgba(255,255,255,0.12)'],
      [1, 'rgba(255,255,255,0)'],
    ]);
    g.fillRect(0, 0, S, S);
  }
}

function drawRingSoft(g, S) {
  const c = S / 2;
  g.fillStyle = radial(g, c, c, c, [
    [0, 'rgba(255,255,255,0)'],
    [0.55, 'rgba(255,255,255,0)'],
    [0.74, 'rgba(255,255,255,1)'],
    [0.9, 'rgba(255,255,255,0.15)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function drawChip(g, S) {
  const c = S / 2;
  g.fillStyle = radial(g, c, c, c * 0.7, [
    [0, 'rgba(255,255,255,1)'],
    [0.55, 'rgba(255,255,255,0.5)'],
    [1, 'rgba(255,255,255,0)'],
  ]);
  g.fillRect(0, 0, S, S);
}

function roundRect(g, x, y, w, h, r) {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}

// ------------------------------------------------------------- atlas build

let _spriteTex = null;

export function spriteAtlas() {
  if (_spriteTex) return _spriteTex;
  const cv = document.createElement('canvas');
  cv.width = SIZE; cv.height = SIZE;
  const g = cv.getContext('2d');
  const rng = makeRng(0x5f4a11);

  const S = CELL - PAD * 2;
  const draw = [
    drawGlow, drawSparkle, drawStreak, drawStar6,
    (c, s) => drawScorch(c, s, rng), drawConfetti, drawShard,
    (c, s) => drawSmoke(c, s, rng), drawRingSoft, drawChip,
  ];
  for (let i = 0; i < draw.length; i++) {
    const cx = (i % GRID) * CELL + PAD;
    const cy = Math.floor(i / GRID) * CELL + PAD;
    g.save();
    g.translate(cx, cy);
    g.beginPath();
    g.rect(0, 0, S, S);
    g.clip();
    draw[i](g, S);
    g.restore();
  }

  const t = new THREE.CanvasTexture(cv);
  t.generateMipmaps = false;
  t.minFilter = THREE.LinearFilter;
  t.magFilter = THREE.LinearFilter;
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.needsUpdate = true;
  _spriteTex = t;
  return t;
}

// -------------------------------------------------------------- word atlas

const WORD_W = 1024;
// One row per word, sized to the whole vocabulary with headroom. The previous
// 512px atlas fitted five rows for a fifteen-word vocabulary, so ten callouts —
// every combo milestone above x5, plus COMBO!/NICE!/ON FIRE!/UNREAL! — resolved
// to no atlas entry and silently never drew.
const WORD_ROW = 112;
const WORD_H = 2048;   // 18 rows
// Painted glyph + stroke reaches ~90px inside a 112px row. Sampling the full
// row with LinearFilter and no guard pulls in the neighbouring word's stroke,
// which reads on screen as a second, offset copy of the callout.
const WORD_PAD = 8;

let _wordTex = null;
/** @type {Map<string, {rect:number[], aspect:number}>} */
const _words = new Map();

/**
 * Render a fixed vocabulary of callouts once.
 * @param {string[]} words
 */
export function wordAtlas(words) {
  if (_wordTex) return { texture: _wordTex, words: _words };
  const cv = document.createElement('canvas');
  cv.width = WORD_W; cv.height = WORD_H;
  const g = cv.getContext('2d');
  const rows = Math.floor(WORD_H / WORD_ROW);

  g.textBaseline = 'middle';
  g.textAlign = 'left';
  g.lineJoin = 'round';

  for (let i = 0; i < words.length && i < rows; i++) {
    const w = words[i];
    let px = 68;
    g.font = `900 ${px}px system-ui, "Arial Black", "Helvetica Neue", Arial, sans-serif`;
    let tw = g.measureText(w).width;
    const maxW = WORD_W - 24;
    if (tw > maxW) {
      px = Math.floor(px * (maxW / tw));
      g.font = `900 ${px}px system-ui, "Arial Black", "Helvetica Neue", Arial, sans-serif`;
      tw = g.measureText(w).width;
    }
    const stroke = Math.max(6, px * 0.14);
    const x = 12;
    const y = i * WORD_ROW + WORD_ROW / 2;
    // black outline -> stays black through the instance tint (see file header)
    g.strokeStyle = '#000000';
    g.lineWidth = stroke * 2;
    g.strokeText(w, x, y);
    g.fillStyle = '#ffffff';
    g.fillText(w, x, y);

    const bw = tw + stroke * 2 + 8;
    const bh = WORD_ROW - WORD_PAD * 2;
    const u0 = (x - stroke - 4) / WORD_W;
    const du = bw / WORD_W;
    const v1 = 1 - (i * WORD_ROW + WORD_PAD) / WORD_H;
    const dv = bh / WORD_H;
    _words.set(w, { rect: [u0, v1 - dv, du, dv], aspect: bw / bh });
  }

  const t = new THREE.CanvasTexture(cv);
  t.generateMipmaps = false;
  t.minFilter = THREE.LinearFilter;
  t.magFilter = THREE.LinearFilter;
  t.colorSpace = THREE.SRGBColorSpace;
  t.needsUpdate = true;
  _wordTex = t;
  return { texture: _wordTex, words: _words };
}
