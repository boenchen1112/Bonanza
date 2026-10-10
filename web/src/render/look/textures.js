/**
 * Procedural pattern textures.  [render agent owns this dir]
 *
 * All greyscale *masks*, never coloured. The palette owns colour; a texture
 * here only says "this part is lighter than that part". That is what lets one
 * ground texture serve six games without a single re-authored asset, and what
 * makes a palette cross-fade actually cross-fade instead of half-changing.
 *
 * Drawn once into a canvas at load and cached forever. No fetches, ever.
 */

import * as THREE from 'three';
import { makeRng } from '../../core/util.js';

const cache = new Map();

function canvasTex(key, size, draw, { repeat = 1, wrap = THREE.RepeatWrapping } = {}) {
  if (cache.has(key)) return cache.get(key);
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d');
  draw(g, size);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = wrap;
  t.repeat.set(repeat, repeat);
  t.anisotropy = 4;
  t.colorSpace = THREE.SRGBColorSpace;
  cache.set(key, t);
  return t;
}

/** Arena floor: concentric rings, a home circle, and radial wedges. */
export function arenaTexture() {
  return canvasTex('arena', 512, (g, S) => {
    const c = S / 2;
    g.fillStyle = '#b4b4b4';
    g.fillRect(0, 0, S, S);

    // alternating wedges — very low contrast, just enough to read rotation
    g.save();
    g.translate(c, c);
    for (let i = 0; i < 16; i++) {
      g.fillStyle = i % 2 ? 'rgba(255,255,255,0.10)' : 'rgba(0,0,0,0.07)';
      g.beginPath();
      g.moveTo(0, 0);
      g.arc(0, 0, c, (i / 16) * Math.PI * 2, ((i + 1) / 16) * Math.PI * 2);
      g.closePath();
      g.fill();
    }
    g.restore();

    // rings
    for (let i = 1; i <= 6; i++) {
      const r = (i / 6) * c * 0.98;
      g.strokeStyle = i % 2 ? 'rgba(255,255,255,0.34)' : 'rgba(255,255,255,0.16)';
      g.lineWidth = i % 2 ? S * 0.012 : S * 0.006;
      g.beginPath(); g.arc(c, c, r, 0, Math.PI * 2); g.stroke();
    }

    // centre plate
    g.fillStyle = 'rgba(255,255,255,0.30)';
    g.beginPath(); g.arc(c, c, c * 0.17, 0, Math.PI * 2); g.fill();
    g.strokeStyle = 'rgba(255,255,255,0.7)';
    g.lineWidth = S * 0.014;
    g.beginPath(); g.arc(c, c, c * 0.17, 0, Math.PI * 2); g.stroke();

    // speckle, kills the plastic-flat look under bloom (seeded: the build is
    // byte-identical every run, which matters for screenshot diffing)
    const rnd = makeRng(0xa11ce);
    for (let i = 0; i < 2600; i++) {
      const x = rnd() * S, y = rnd() * S;
      g.fillStyle = rnd() < 0.5 ? 'rgba(255,255,255,0.06)' : 'rgba(0,0,0,0.06)';
      g.fillRect(x, y, 2, 2);
    }
  }, { repeat: 1, wrap: THREE.ClampToEdgeWrapping });
}

/** Wide diagonal stripes for banners and risers. */
export function stripeTexture() {
  return canvasTex('stripe', 128, (g, S) => {
    g.fillStyle = '#ffffff';
    g.fillRect(0, 0, S, S);
    g.fillStyle = 'rgba(0,0,0,0.22)';
    for (let i = -S; i < S * 2; i += 32) {
      g.save(); g.translate(i, 0); g.rotate(-0.5);
      g.fillRect(0, -S, 16, S * 3);
      g.restore();
    }
  }, { repeat: 1 });
}

/** Crowd stand mask: rows of seats. */
export function seatTexture() {
  return canvasTex('seats', 256, (g, S) => {
    g.fillStyle = '#8a8a8a';
    g.fillRect(0, 0, S, S);
    const rows = 8, cols = 12;
    for (let r = 0; r < rows; r++) {
      for (let cI = 0; cI < cols; cI++) {
        const x = (cI + (r % 2 ? 0.5 : 0)) * (S / cols);
        const y = r * (S / rows);
        g.fillStyle = 'rgba(255,255,255,0.35)';
        g.fillRect(x + 2, y + 2, S / cols - 5, S / rows - 6);
      }
    }
    g.fillStyle = 'rgba(0,0,0,0.25)';
    for (let r = 0; r < rows; r++) g.fillRect(0, r * (S / rows), S, 2);
  }, { repeat: 1 });
}

/** Soft round gradient — light shafts, glows, confetti dots. */
export function glowTexture() {
  return canvasTex('glow', 128, (g, S) => {
    const gr = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
    gr.addColorStop(0, 'rgba(255,255,255,1)');
    gr.addColorStop(0.35, 'rgba(255,255,255,0.55)');
    gr.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = gr;
    g.fillRect(0, 0, S, S);
  }, { repeat: 1, wrap: THREE.ClampToEdgeWrapping });
}

export function disposeTextures() {
  for (const t of cache.values()) t.dispose();
  cache.clear();
}
