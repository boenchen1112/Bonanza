/**
 * Procedural character rig.  [chars agent owns this directory]
 *
 * Everything here is generated from Three.js primitives — no model files, no
 * textures loaded from disk, no network. A character is a plain THREE.Group
 * with a *named joint hierarchy* hanging off it, so `anim.js` can pose it
 * without knowing anything about how it was assembled.
 *
 * Design rules, in priority order:
 *
 *  1. **Silhouette first.** A player reads a character at 1/10th of a second
 *     from a fixed camera. So the four builds differ in HEIGHT, WIDTH and
 *     HEAD-TO-BODY RATIO — not in decoration. Stand any two of them side by
 *     side as black shapes and you can still name them.
 *  2. **Chunky and toy-like.** Rounded boxes and spheres, thick limbs, a head
 *     between 1/3 and 1/2 of total height. Nothing thinner than it needs to be:
 *     thin limbs vanish at distance and vanish harder in motion blur.
 *  3. **The face is a readout, not a portrait.** Eyes, pupils, two independent
 *     brows and a curve-signed mouth. Those four channels carry every emotion
 *     the game needs, and each is one cheap transform.
 *  4. **Cost is fixed and known.** 20 draw calls per character at full detail,
 *     15 at 'lite'. Geometry and materials are cached across characters, so a
 *     four-player roster allocates four builds' worth of geometry once.
 *
 * Coordinate convention: feet at y=0, character faces +Z (toward the camera).
 * Limb segments hang along -Y from their joint, so a rotation of 0 is "arm
 * straight down". `anim.js` talks in forward-positive angles and does the sign
 * flip, so nothing downstream has to remember this paragraph.
 */

import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { makeRng } from '../core/util.js';

// ---------------------------------------------------------------- palettes

/**
 * Palettes are chosen for *value* contrast, not just hue. Two players who
 * both read as "mid-tone saturated" are indistinguishable in a screenshot of
 * a fast moment, which is the only screenshot that matters. So each palette
 * pairs a bright body with a dark trim and a light head.
 */
export const PALETTES = [
  {
    id: 'ember', name: 'Ember',
    body: 0xff5d3a, trim: 0x7a1f10, skin: 0xffe0c2, limb: 0xff8a5c,
    accent: 0xffd93d, eye: 0x2a1206, rim: 0xff9a5c,
  },
  {
    id: 'lagoon', name: 'Lagoon',
    body: 0x35c8f0, trim: 0x0d3f66, skin: 0xdff6ff, limb: 0x6fdcff,
    accent: 0xb4ff6a, eye: 0x05202e, rim: 0x8ae6ff,
  },
  {
    id: 'orchid', name: 'Orchid',
    body: 0xd94ee0, trim: 0x4a0f56, skin: 0xffe6ff, limb: 0xf07dff,
    accent: 0xffd93d, eye: 0x28062e, rim: 0xf59aff,
  },
  {
    id: 'lime', name: 'Lime',
    body: 0x9ee87a, trim: 0x1f4d1a, skin: 0xf4ffd9, limb: 0xc4f59c,
    accent: 0xff5d73, eye: 0x0e2a0a, rim: 0xd6ff9e,
  },
  {
    id: 'sunburst', name: 'Sunburst',
    body: 0xffc32b, trim: 0x6b3a00, skin: 0xfff2cf, limb: 0xffd965,
    accent: 0x4dd6ff, eye: 0x2e1a00, rim: 0xffe08a,
  },
  {
    id: 'ultramarine', name: 'Ultramarine',
    body: 0x5566ff, trim: 0x141a52, skin: 0xdfe4ff, limb: 0x8f9bff,
    accent: 0xffd93d, eye: 0x0a0d33, rim: 0x9fa9ff,
  },
  {
    id: 'coral', name: 'Coral',
    body: 0xff7fa8, trim: 0x6b1533, skin: 0xffe9f0, limb: 0xffa8c4,
    accent: 0x9ee87a, eye: 0x2e0a18, rim: 0xffb6cd,
  },
  {
    id: 'slate', name: 'Slate',
    body: 0x8f9bb3, trim: 0x232838, skin: 0xeef2ff, limb: 0xb6c0d6,
    accent: 0xff5d73, eye: 0x10131f, rim: 0xc9d3e8,
  },
];

/** Stable palette for player index / any integer. Never returns undefined. */
export function paletteFor(i = 0) {
  return PALETTES[((i | 0) % PALETTES.length + PALETTES.length) % PALETTES.length];
}

export function paletteById(id) {
  return PALETTES.find((p) => p.id === id) || PALETTES[0];
}

// ------------------------------------------------------------------ builds

/**
 * Four builds. The numbers below are the whole design: heights 1.05 / 1.35 /
 * 1.58 / 1.98 and head-to-height ratios 0.40 / 0.27 / 0.27 / 0.18 mean the
 * roster silhouettes are separated on two axes at once.
 */
export const BUILDS = {
  /** Squat, enormous head, stubby limbs. The rookie. */
  small: {
    id: 'small',
    head: { w: 0.46, h: 0.44, d: 0.44, shape: 'sphere' },
    torso: { w: 0.44, h: 0.30, d: 0.36, taper: 1.10 },
    arm: { upper: 0.16, fore: 0.14, r: 0.085 },
    leg: { thigh: 0.11, shin: 0.10, r: 0.095 },
    stance: 0.145, shoulder: 0.24, neck: 0.03,
    gear: 'pom', bobbleR: 0.115, bobbleY: 0.30,
    footL: 0.20, handR: 0.105,
  },
  /** Broad, low, heavy. Reads as power even standing still. */
  wide: {
    id: 'wide',
    head: { w: 0.46, h: 0.34, d: 0.38, shape: 'box' },
    torso: { w: 0.86, h: 0.46, d: 0.54, taper: 0.82 },
    arm: { upper: 0.22, fore: 0.20, r: 0.125 },
    leg: { thigh: 0.16, shin: 0.14, r: 0.135 },
    stance: 0.30, shoulder: 0.44, neck: 0.03,
    gear: 'pads', bobbleR: 0.075, bobbleY: 0.22,
    footL: 0.26, handR: 0.145,
  },
  /** The default. Round, friendly, bouncy. */
  round: {
    id: 'round',
    head: { w: 0.48, h: 0.44, d: 0.46, shape: 'sphere' },
    torso: { w: 0.60, h: 0.48, d: 0.48, taper: 0.92 },
    arm: { upper: 0.24, fore: 0.22, r: 0.098 },
    leg: { thigh: 0.19, shin: 0.17, r: 0.115 },
    stance: 0.20, shoulder: 0.32, neck: 0.045,
    gear: 'visor', bobbleR: 0.085, bobbleY: 0.29,
    footL: 0.24, handR: 0.115,
  },
  /** Lanky, high shoulders, small head. Long limbs = long, legible arcs. */
  tall: {
    id: 'tall',
    head: { w: 0.34, h: 0.40, d: 0.36, shape: 'egg' },
    torso: { w: 0.40, h: 0.58, d: 0.36, taper: 1.14 },
    arm: { upper: 0.36, fore: 0.32, r: 0.072 },
    leg: { thigh: 0.34, shin: 0.30, r: 0.082 },
    stance: 0.15, shoulder: 0.22, neck: 0.075,
    gear: 'crest', bobbleR: 0.07, bobbleY: 0.34,
    footL: 0.22, handR: 0.09,
  },
};

export const BUILD_IDS = ['round', 'tall', 'small', 'wide'];

export function buildFor(i = 0) {
  return BUILDS[BUILD_IDS[((i | 0) % BUILD_IDS.length + BUILD_IDS.length) % BUILD_IDS.length]];
}

// ------------------------------------------------------- shared resources
//
// Geometry and materials are keyed and cached at module scope. A minigame that
// spawns four characters of two builds allocates two builds' worth of shapes,
// and a scene change that respawns the same cast allocates nothing at all.
// `disposeSharedResources()` exists for teardown/tests; ordinary `dispose()`
// on a character deliberately does NOT touch the cache.

const geoCache = new Map();
const matCache = new Map();
let shadowTex = null;

function geo(key, make) {
  let g = geoCache.get(key);
  if (!g) { g = make(); geoCache.set(key, g); }
  return g;
}

/**
 * Rim light baked into the material.
 *
 * A dark inverted-hull outline would cost one extra draw call per part — 19
 * more per character — which we cannot afford. A fresnel rim in the shader
 * costs nothing and does most of the same job: it draws a bright edge exactly
 * where the silhouette is, so the character separates from the backdrop even
 * when the backdrop is the same hue.
 */
function patchRim(mat, rimHex, strength) {
  const uRimColor = { value: new THREE.Color(rimHex) };
  const uRimStr = { value: strength };
  mat.onBeforeCompile = (shader) => {
    shader.uniforms.uRimColor = uRimColor;
    shader.uniforms.uRimStr = uRimStr;
    shader.fragmentShader = shader.fragmentShader
      .replace(
        'void main() {',
        'uniform vec3 uRimColor;\nuniform float uRimStr;\nvoid main() {'
      )
      .replace(
        '#include <opaque_fragment>',
        [
          'float bbbRim = 1.0 - clamp(dot(normal, normalize(vViewPosition)), 0.0, 1.0);',
          'outgoingLight += uRimColor * pow(bbbRim, 2.6) * uRimStr;',
          '#include <opaque_fragment>',
        ].join('\n')
      );
  };
  mat.userData.rim = { uRimColor, uRimStr };
  return mat;
}

/**
 * Merge that actually works on mixed inputs.
 *
 * `RoundedBoxGeometry` is non-indexed while `SphereGeometry`/`CapsuleGeometry`
 * are indexed, and `mergeGeometries` silently returns null (plus a console
 * error) when it is handed both. Normalising to non-indexed first costs a few
 * duplicated vertices on shapes this small and removes the whole class of bug.
 */
function mergeParts(parts) {
  if (!parts.length) return new THREE.BufferGeometry();
  if (parts.length === 1) return parts[0];
  const anyIndexed = parts.some((g) => g.index !== null);
  const anyDirect = parts.some((g) => g.index === null);
  const list = (anyIndexed && anyDirect)
    ? parts.map((g) => (g.index ? g.toNonIndexed() : g))
    : parts;
  const merged = mergeGeometries(list, false);
  return merged || parts[0];
}

function mat(key, make) {
  let m = matCache.get(key);
  if (!m) { m = make(); matCache.set(key, m); }
  return m;
}

function bodyMat(pal, role, hex, opts = {}) {
  return mat(`${pal.id}:${role}`, () => patchRim(new THREE.MeshStandardMaterial({
    color: hex,
    roughness: opts.roughness ?? 0.52,
    metalness: opts.metalness ?? 0.02,
  }), pal.rim, opts.rim ?? 0.5));
}

function flatMat(key, hex, opts = {}) {
  return mat(`flat:${key}`, () => new THREE.MeshBasicMaterial({
    color: hex, toneMapped: opts.toneMapped ?? false,
  }));
}

function getShadowTexture() {
  if (shadowTex) return shadowTex;
  const S = 64;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
  // Soft-but-defined: a hard core sells contact, the falloff sells the height.
  grad.addColorStop(0.0, 'rgba(0,0,0,0.85)');
  grad.addColorStop(0.45, 'rgba(0,0,0,0.55)');
  grad.addColorStop(1.0, 'rgba(0,0,0,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, S, S);
  shadowTex = new THREE.CanvasTexture(c);
  shadowTex.colorSpace = THREE.SRGBColorSpace;
  return shadowTex;
}

/** Tear down every cached geometry/material/texture. Tests and hard resets. */
export function disposeSharedResources() {
  for (const g of geoCache.values()) g.dispose();
  for (const m of matCache.values()) m.dispose();
  geoCache.clear();
  matCache.clear();
  if (shadowTex) { shadowTex.dispose(); shadowTex = null; }
}

// --------------------------------------------------------- shape factories

/** A capsule-ish limb segment hanging from the joint at its top, along -Y. */
function limbGeo(key, len, r, taperEnd = 1) {
  return geo(`limb:${key}:${len.toFixed(3)}:${r.toFixed(3)}:${taperEnd.toFixed(2)}`, () => {
    const g = new THREE.CapsuleGeometry(r, Math.max(0.001, len - r * 0.6), 3, 10);
    g.translate(0, -(len * 0.5), 0);
    if (taperEnd !== 1) {
      // Squeeze the far end so limbs read as tapered rather than as sausages.
      const pos = g.attributes.position;
      const lo = -len, hi = 0;
      for (let i = 0; i < pos.count; i++) {
        const y = pos.getY(i);
        const t = THREE.MathUtils.clamp((y - hi) / (lo - hi), 0, 1);
        const s = 1 + (taperEnd - 1) * t;
        pos.setX(i, pos.getX(i) * s);
        pos.setZ(i, pos.getZ(i) * s);
      }
      pos.needsUpdate = true;
      g.computeVertexNormals();
    }
    return g;
  });
}

function ballGeo(key, r, seg = 14) {
  return geo(`ball:${key}:${r.toFixed(3)}:${seg}`, () => new THREE.SphereGeometry(r, seg + 4, seg));
}

function roundBoxGeo(key, w, h, d, r) {
  return geo(`rbox:${key}:${w.toFixed(3)}:${h.toFixed(3)}:${d.toFixed(3)}:${r.toFixed(3)}`,
    () => new RoundedBoxGeometry(w, h, d, 3, Math.min(r, Math.min(w, h, d) * 0.49)));
}

/** Torso: a rounded box, tapered top-vs-bottom, with the pelvis merged in. */
function torsoGeo(b) {
  return geo(`torso:${b.id}`, () => {
    const t = b.torso;
    const chest = new RoundedBoxGeometry(t.w, t.h, t.d, 3, Math.min(t.w, t.h, t.d) * 0.42);
    // Taper: >1 = wider at the shoulders (tall/small), <1 = wider at the hips.
    const pos = chest.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      const y = pos.getY(i);
      const u = THREE.MathUtils.clamp(y / t.h + 0.5, 0, 1); // 0 hips .. 1 shoulders
      const s = 1 / t.taper + (t.taper - 1 / t.taper) * u;
      pos.setX(i, pos.getX(i) * s);
      pos.setZ(i, pos.getZ(i) * s);
    }
    pos.needsUpdate = true;
    chest.computeVertexNormals();
    chest.translate(0, t.h * 0.5, 0);

    // Pelvis block, so the hip joints have something to come out of.
    const pelvis = new RoundedBoxGeometry(t.w * 0.86 / t.taper, t.h * 0.34, t.d * 0.9 / t.taper, 2,
      Math.min(t.w, t.d) * 0.22);
    pelvis.translate(0, -t.h * 0.10, 0);
    return mergeParts([chest, pelvis]);
  });
}

function headGeo(b) {
  return geo(`head:${b.id}`, () => {
    const h = b.head;
    if (h.shape === 'box') {
      const g = new RoundedBoxGeometry(h.w, h.h, h.d, 4, Math.min(h.w, h.h, h.d) * 0.34);
      return g;
    }
    const g = new THREE.SphereGeometry(0.5, 22, 16);
    if (h.shape === 'egg') {
      // Pinch the bottom: an egg head reads instantly as "the tall one".
      const pos = g.attributes.position;
      for (let i = 0; i < pos.count; i++) {
        const y = pos.getY(i);
        const s = 1 - 0.34 * THREE.MathUtils.clamp(-y * 2, 0, 1);
        pos.setX(i, pos.getX(i) * s);
        pos.setZ(i, pos.getZ(i) * s);
      }
      pos.needsUpdate = true;
      g.computeVertexNormals();
    }
    g.scale(h.w, h.h, h.d);
    return g;
  });
}

/** Static headgear per build — merged into ONE mesh so it costs one call. */
function gearGeo(b) {
  return geo(`gear:${b.id}`, () => {
    const h = b.head;
    const parts = [];
    if (b.gear === 'visor') {
      const band = new THREE.CylinderGeometry(h.w * 0.53, h.w * 0.53, h.h * 0.20, 18, 1, true);
      band.rotateX(Math.PI / 2);
      band.scale(1, 1, h.d / h.w);
      band.rotateX(-Math.PI / 2);
      band.translate(0, h.h * 0.16, 0);
      parts.push(band);
      const brim = new RoundedBoxGeometry(h.w * 0.86, h.h * 0.07, h.d * 0.42, 2, 0.02);
      brim.translate(0, h.h * 0.10, h.d * 0.46);
      parts.push(brim);
    } else if (b.gear === 'crest') {
      // A dorsal fin. Three fins of falling height = unmistakable profile.
      for (let i = 0; i < 3; i++) {
        const s = 1 - i * 0.26;
        const fin = new THREE.ConeGeometry(h.w * 0.16 * s, h.h * 0.55 * s, 4);
        fin.rotateY(Math.PI / 4);
        fin.translate(0, h.h * 0.48 + h.h * 0.24 * s, -h.d * 0.16 * i);
        parts.push(fin);
      }
    } else if (b.gear === 'pom') {
      const stalk = new THREE.CylinderGeometry(h.w * 0.055, h.w * 0.07, h.h * 0.30, 7);
      stalk.translate(0, h.h * 0.58, 0);
      parts.push(stalk);
      const ring = new THREE.TorusGeometry(h.w * 0.46, h.w * 0.055, 5, 16);
      ring.rotateX(Math.PI / 2);
      ring.scale(1, 1, h.d / h.w);
      ring.translate(0, h.h * 0.30, 0);
      parts.push(ring);
    } else if (b.gear === 'pads') {
      for (const sx of [-1, 1]) {
        const pad = new THREE.SphereGeometry(b.torso.w * 0.30, 12, 8, 0, Math.PI * 2, 0, Math.PI * 0.62);
        pad.scale(1, 0.7, 1);
        pad.translate(sx * b.shoulder * 1.02, 0, 0);
        parts.push(pad);
      }
    }
    return mergeParts(parts);
  });
}

/** Two eye whites merged into one mesh. Blink = scale.y of the whole thing. */
function eyesGeo(b, sep, r) {
  return geo(`eyes:${b.id}:${sep.toFixed(3)}:${r.toFixed(3)}`, () => {
    const parts = [];
    for (const sx of [-1, 1]) {
      const g = new THREE.SphereGeometry(r, 14, 10);
      g.scale(1, 1.12, 0.62);
      g.translate(sx * sep, 0, 0);
      parts.push(g);
    }
    return mergeParts(parts);
  });
}

function pupilsGeo(b, sep, r) {
  return geo(`pupils:${b.id}:${sep.toFixed(3)}:${r.toFixed(3)}`, () => {
    const parts = [];
    for (const sx of [-1, 1]) {
      const g = new THREE.SphereGeometry(r, 12, 8);
      g.scale(1, 1.15, 0.5);
      g.translate(sx * sep, 0, 0);
      parts.push(g);
    }
    return mergeParts(parts);
  });
}

/**
 * Mouth: two straight bars hinged at the outer corners.
 *
 * The obvious implementation — one half-torus flipped by a negative Y scale —
 * collapses to nothing as it passes through "neutral", so the mouth blinks out
 * of existence during every smile→frown blend. Hinging two bars instead gives
 * one continuous `curve` channel: +1 smile, 0 dead-flat, -1 frown, and the
 * mouth is always visible at every value in between.
 *
 * `side` is -1 for the left bar (extends inward, +x) and +1 for the right.
 */
function mouthBarGeo(b, len, side) {
  return geo(`mouthbar:${b.id}:${len.toFixed(3)}:${side}`, () => {
    const g = new RoundedBoxGeometry(len, len * 0.30, len * 0.30, 2, len * 0.14);
    g.translate(-side * len * 0.5, 0, 0);
    return g;
  });
}

// ------------------------------------------------------------ construction

let uid = 0;

/**
 * Build a character.
 *
 * @param {object} [opts]
 * @param {object|string|number} [opts.palette] palette object, id, or index
 * @param {object|string|number} [opts.build]   build object, id, or index
 * @param {number} [opts.seed]    deterministic per-character variation
 * @param {'full'|'lite'} [opts.detail]  19 draw calls vs 11
 * @param {number} [opts.scale]   uniform world scale
 * @param {string} [opts.name]
 * @returns {THREE.Group} with `.joints`, `.dims`, `.palette`, `.build`,
 *   `.attach(jointName, obj)` and `.dispose()`.
 */
export function makeCharacter({
  palette = 0, build = 'round', seed = 1, detail = 'full', scale = 1, name = '',
} = {}) {
  const pal = typeof palette === 'object' && palette ? palette
    : typeof palette === 'string' ? paletteById(palette) : paletteFor(palette);
  const b = typeof build === 'object' && build ? build
    : typeof build === 'string' ? (BUILDS[build] || BUILDS.round) : buildFor(build);
  const rng = makeRng(seed >>> 0 || 1);
  const lite = detail === 'lite';

  const group = new THREE.Group();
  group.name = name || `char:${b.id}:${pal.id}:${uid++}`;

  // --- materials -----------------------------------------------------------
  const mBody = bodyMat(pal, 'body', pal.body);
  const mLimb = bodyMat(pal, 'limb', pal.limb);
  const mSkin = bodyMat(pal, 'skin', pal.skin, { roughness: 0.62 });
  const mTrim = bodyMat(pal, 'trim', pal.trim, { roughness: 0.4, rim: 0.9 });
  const mAccent = bodyMat(pal, 'accent', pal.accent, { roughness: 0.3, rim: 0.7 });
  const mEye = flatMat(`eye:${pal.id}`, pal.eye);
  const mWhite = flatMat('white', 0xffffff);
  const mMouth = flatMat(`mouth:${pal.id}`, pal.eye);
  const mGape = flatMat('gape', 0x3a0d1a);

  // --- dimensions ----------------------------------------------------------
  const legLen = b.leg.thigh + b.leg.shin;
  const hipY = legLen + b.leg.r * 0.35;
  const shoulderY = hipY + b.torso.h * 0.86;
  const headY = hipY + b.torso.h + b.neck + b.head.h * 0.5;
  const height = headY + b.head.h * 0.5;

  // --- nodes ---------------------------------------------------------------
  // `root` is the squash/stretch node: scaling it scales the whole character
  // about its feet, which is the only pivot that makes squash read as weight.
  const root = new THREE.Group(); root.name = 'root';
  const hips = new THREE.Group(); hips.name = 'hips';
  const torso = new THREE.Group(); torso.name = 'torso';
  const head = new THREE.Group(); head.name = 'head';

  hips.position.y = hipY;
  torso.position.y = 0;
  head.position.y = b.torso.h + b.neck;

  group.add(root);
  root.add(hips);
  hips.add(torso);
  torso.add(head);

  // --- shadow (sibling of root: it must NOT squash with the body) ----------
  const shadow = new THREE.Mesh(
    geo('shadowPlane', () => new THREE.PlaneGeometry(1, 1)),
    new THREE.MeshBasicMaterial({
      map: getShadowTexture(), transparent: true, depthWrite: false,
      opacity: 0.55, color: 0x000000, toneMapped: false,
    })
  );
  shadow.rotation.x = -Math.PI / 2;
  shadow.position.y = 0.012;
  shadow.scale.setScalar(Math.max(b.torso.w, b.stance * 2.6) * 1.9);
  shadow.renderOrder = -1;
  shadow.name = 'shadow';
  group.add(shadow);

  // --- torso mesh ----------------------------------------------------------
  const torsoMesh = new THREE.Mesh(torsoGeo(b), mBody);
  torsoMesh.name = 'torsoMesh';
  torso.add(torsoMesh);

  // --- head ----------------------------------------------------------------
  const headMesh = new THREE.Mesh(headGeo(b), mSkin);
  headMesh.name = 'headMesh';
  head.add(headMesh);

  let gear = null;
  if (!lite) {
    gear = new THREE.Mesh(gearGeo(b), b.gear === 'pads' ? mTrim : mAccent);
    gear.name = 'gear';
    if (b.gear === 'pads') { torso.add(gear); gear.position.y = b.torso.h * 0.86; }
    else head.add(gear);
  }

  // Bobble: a springy appendage. It is the best aliveness-per-draw-call in the
  // rig — it keeps moving for a beat after the body stops, which is the single
  // cheapest way to make a character look animated rather than posed.
  const bobble = new THREE.Group(); bobble.name = 'bobble';
  bobble.position.y = b.bobbleY;
  head.add(bobble);
  let bobbleMesh = null;
  if (!lite) {
    bobbleMesh = new THREE.Mesh(ballGeo('bob', b.bobbleR, 10), mAccent);
    bobbleMesh.name = 'bobbleMesh';
    bobble.add(bobbleMesh);
  }

  // --- face ----------------------------------------------------------------
  const faceZ = b.head.d * 0.47;
  const eyeSep = b.head.w * 0.235;
  const eyeR = b.head.w * 0.155;
  const eyeY = b.head.h * 0.06;

  const face = new THREE.Group(); face.name = 'face';
  face.position.set(0, eyeY, 0);
  head.add(face);

  const eyes = new THREE.Mesh(eyesGeo(b, eyeSep, eyeR), mWhite);
  eyes.name = 'eyes';
  eyes.position.z = faceZ;
  face.add(eyes);

  const pupils = new THREE.Mesh(pupilsGeo(b, eyeSep, eyeR * 0.56), mEye);
  pupils.name = 'pupils';
  pupils.position.z = faceZ + eyeR * 0.42;
  face.add(pupils);

  const mouthHalf = b.head.w * 0.20;
  const mouth = new THREE.Group();
  mouth.name = 'mouth';
  mouth.position.set(0, -b.head.h * 0.24, faceZ + eyeR * 0.06);
  face.add(mouth);
  const mouthL = new THREE.Mesh(mouthBarGeo(b, mouthHalf, -1), mMouth);
  const mouthR = new THREE.Mesh(mouthBarGeo(b, mouthHalf, 1), mMouth);
  mouthL.name = 'mouthL'; mouthR.name = 'mouthR';
  mouthL.position.x = -mouthHalf;
  mouthR.position.x = mouthHalf;
  mouth.add(mouthL, mouthR);

  let browL = null, browR = null, gape = null;
  if (!lite) { // eslint-disable-line
    // Brows are separate meshes on purpose: mirrored rotation is what turns
    // "surprised" into "furious", and one merged mesh cannot mirror.
    const browGeo = roundBoxGeo(`brow:${b.id}`, eyeR * 1.9, eyeR * 0.46, eyeR * 0.5, eyeR * 0.2);
    browL = new THREE.Mesh(browGeo, mTrim);
    browR = new THREE.Mesh(browGeo, mTrim);
    browL.name = 'browL'; browR.name = 'browR';
    browL.position.set(-eyeSep, eyeR * 1.5, faceZ + eyeR * 0.2);
    browR.position.set(eyeSep, eyeR * 1.5, faceZ + eyeR * 0.2);
    face.add(browL, browR);

    gape = new THREE.Mesh(ballGeo('gape', b.head.w * 0.17, 10), mGape);
    gape.name = 'gape';
    gape.position.copy(mouth.position);
    gape.position.z -= b.head.w * 0.03;
    gape.scale.setScalar(0.001);
    face.add(gape);
  }

  // --- limbs ---------------------------------------------------------------
  function makeArm(side) {
    const sx = side === 'L' ? -1 : 1;
    const upper = new THREE.Group(); upper.name = `arm${side}.upper`;
    upper.position.set(sx * b.shoulder, b.torso.h * 0.80, 0);
    torso.add(upper);
    upper.add(new THREE.Mesh(limbGeo(`ua:${b.id}`, b.arm.upper, b.arm.r, 0.9), mLimb));

    const fore = new THREE.Group(); fore.name = `arm${side}.fore`;
    fore.position.y = -b.arm.upper;
    upper.add(fore);
    // Forearm and hand share a mesh: the wrist is not a joint anyone can see
    // on a character this chunky, and it buys back two draw calls per rig.
    const foreMesh = new THREE.Mesh(
      geo(`foreArm:${b.id}`, () => {
        const arm = new THREE.CapsuleGeometry(b.arm.r * 0.92, Math.max(0.001, b.arm.fore - b.arm.r * 0.6), 3, 10);
        arm.translate(0, -b.arm.fore * 0.5, 0);
        const hand = new THREE.SphereGeometry(b.handR, 12, 9);
        hand.scale(1, 0.92, 1.06);
        hand.translate(0, -b.arm.fore, 0);
        return mergeParts([arm, hand]);
      }), mSkin);
    fore.add(foreMesh);

    const hand = new THREE.Group(); hand.name = `hand${side}`;
    hand.position.y = -b.arm.fore;
    fore.add(hand);
    return { upper, fore, hand, sx };
  }

  function makeLeg(side) {
    const sx = side === 'L' ? -1 : 1;
    const thigh = new THREE.Group(); thigh.name = `leg${side}.thigh`;
    thigh.position.set(sx * b.stance, 0, 0);
    hips.add(thigh);
    thigh.add(new THREE.Mesh(limbGeo(`th:${b.id}`, b.leg.thigh, b.leg.r, 0.88), mLimb));

    const shin = new THREE.Group(); shin.name = `leg${side}.shin`;
    shin.position.y = -b.leg.thigh;
    thigh.add(shin);
    const shinMesh = new THREE.Mesh(
      geo(`shinFoot:${b.id}`, () => {
        const leg = new THREE.CapsuleGeometry(b.leg.r * 0.86, Math.max(0.001, b.leg.shin - b.leg.r * 0.5), 3, 10);
        leg.translate(0, -b.leg.shin * 0.5, 0);
        const foot = new RoundedBoxGeometry(b.leg.r * 1.9, b.leg.r * 0.86, b.footL, 2, b.leg.r * 0.34);
        foot.translate(0, -b.leg.shin - b.leg.r * 0.2, b.footL * 0.24);
        return mergeParts([leg, foot]);
      }), mTrim);
    shin.add(shinMesh);

    const foot = new THREE.Group(); foot.name = `foot${side}`;
    foot.position.y = -b.leg.shin;
    shin.add(foot);
    return { thigh, shin, foot, sx };
  }

  const armL = makeArm('L');
  const armR = makeArm('R');
  const legL = makeLeg('L');
  const legR = makeLeg('R');

  // --- assemble ------------------------------------------------------------
  const joints = {
    root, hips, torso, head, face, bobble, shadow, gear,
    armL, armR, legL, legR,
    eyes, pupils, mouth, mouthL, mouthR, browL, browR, gape,
    torsoMesh, headMesh, bobbleMesh,
  };

  group.scale.setScalar(scale);

  // Shadow casting (the house shadow map only runs where a scene declares a
  // focus, so none of this costs anything elsewhere). By default only the
  // torso and head cast — 2 draws per character, which keeps a five-strong
  // parade inside the 120-draw budget; with the blob underneath as the
  // contact cue that reads as a grounded body. A scene's hero can ask for
  // the whole silhouette with `setShadowDetail('full')`.
  const setShadowDetail = (level = 'core') => {
    group.traverse((o) => { if (o.isMesh) o.castShadow = level === 'full' && o !== shadow; });
    face.traverse((o) => { if (o.isMesh) o.castShadow = false; });
    torsoMesh.castShadow = headMesh.castShadow = level !== 'none';
  };
  setShadowDetail('core');
  group.setShadowDetail = setShadowDetail;

  group.joints = joints;
  group.palette = pal;
  group.build = b;
  group.detail = detail;
  group.seed = seed;
  group.dims = { height, hipY, shoulderY, headY, legLen, scale, headR: b.head.h * 0.5 };
  /** Per-character deterministic jitter so a crowd of clones is not a clone. */
  group.variation = {
    phase: rng() * Math.PI * 2,
    bounce: 0.9 + rng() * 0.25,
    lean: (rng() - 0.5) * 0.06,
    blinkOffset: rng() * 4,
    swagger: rng(),
  };

  /** Parent an object to a named joint (`handR`, `head`, `hips`, …). */
  group.attach = (jointName, obj) => {
    const map = {
      root, hips, torso, head, face, bobble,
      handL: armL.hand, handR: armR.hand,
      footL: legL.foot, footR: legR.foot,
    };
    const target = map[jointName];
    if (!target) return null;
    target.add(obj);
    return target;
  };

  /**
   * Remove from the scene and free the resources that are NOT shared. Cached
   * geometry/materials survive on purpose — see the note at the top.
   */
  group.dispose = () => {
    group.removeFromParent();
    shadow.material.dispose();
  };

  return group;
}

/**
 * Draw-call cost of one character, so callers can budget before building.
 * full: shadow, torso, head, gear, bobble, eyes, pupils, mouth×2, gape,
 *       brow×2, arm×4, leg×4 = 20.
 * lite: drops gear, bobble, gape and both brows = 15.
 */
export function drawCallsFor(detail = 'full') {
  return detail === 'lite' ? 15 : 20;
}

/**
 * One draw for the contact blobs of several characters, instead of one each.
 *
 * Each rig's own blob is hidden and keeps being animated as before (anim.js
 * still scales it and sets its opacity from the jump height); `update()`
 * copies that into an instance per character. Call it once per frame after
 * the animators and any position changes. The returned mesh must be added
 * to the scene by the caller; any parent transform is accounted for.
 * A five-character scene saves 4 draw calls.
 *
 * @param {THREE.Object3D[]} chars  toy-rig characters (others are skipped)
 */
export function batchBlobShadows(chars) {
  const list = chars.filter((c) => c?.joints?.shadow);
  const n = list.length;
  const geo = new THREE.PlaneGeometry(1, 1);
  const alpha = new THREE.InstancedBufferAttribute(new Float32Array(n), 1);
  alpha.setUsage(THREE.DynamicDrawUsage);
  geo.setAttribute('iAlpha', alpha);
  // Same look as the per-rig blob material, plus a per-instance opacity.
  const material = new THREE.MeshBasicMaterial({
    map: getShadowTexture(), transparent: true, depthWrite: false,
    opacity: 1, color: 0x000000, toneMapped: false,
  });
  material.onBeforeCompile = (sh) => {
    sh.vertexShader = 'attribute float iAlpha;\nvarying float vIAlpha;\n'
      + sh.vertexShader.replace('void main() {', 'void main() {\n  vIAlpha = iAlpha;');
    sh.fragmentShader = 'varying float vIAlpha;\n'
      + sh.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n  diffuseColor.a *= vIAlpha;');
  };
  material.customProgramCacheKey = () => 'bbb-blob-alpha';

  const mesh = new THREE.InstancedMesh(geo, material, Math.max(1, n));
  mesh.count = n;
  mesh.name = 'blobShadows';
  mesh.frustumCulled = false;
  mesh.renderOrder = -1;
  mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  for (const c of list) c.joints.shadow.visible = false;

  const inv = new THREE.Matrix4();
  const m4 = new THREE.Matrix4();

  function update() {
    if (mesh.parent) {
      mesh.parent.updateWorldMatrix(true, false);
      inv.copy(mesh.parent.matrixWorld).invert();
    } else {
      inv.identity();
    }
    for (let i = 0; i < n; i++) {
      const c = list[i];
      const blob = c.joints.shadow;
      blob.updateWorldMatrix(true, false);
      mesh.setMatrixAt(i, m4.multiplyMatrices(inv, blob.matrixWorld));
      alpha.array[i] = c.visible && c.parent ? blob.material.opacity : 0;
    }
    mesh.instanceMatrix.needsUpdate = true;
    alpha.needsUpdate = true;
  }

  function dispose() {
    mesh.removeFromParent();
    for (const c of list) c.joints.shadow.visible = true;
    geo.dispose();
    material.dispose();
    mesh.dispose();
  }

  update();
  return { mesh, update, dispose, material };
}

/**
 * One draw (plus one shadow draw) for the crests of a whole lineup, instead of
 * one each — a four-character party lineup was paying ~8 draws for them.
 *
 * Crests differ in shape and colour per character and ride different joints
 * (the springy `bobble`, or the head), so this is a rigid SkinnedMesh rather
 * than an InstancedMesh: every crest's geometry goes into one buffer, bound
 * with weight 1 to its own Bone, and each Bone sits exactly where the crest
 * mesh sat, on the same joint. The rig's animation moves the bones like it
 * moved the crests, with no per-frame work of ours; the renderer's own
 * skeleton update uploads the matrices. Colour moves to vertex colours
 * (linear, straight from each crest material), and a two-sided crest (the
 * fin) gets a back-facing copy so the whole batch can stay FrontSide.
 *
 * The original crest meshes (tagged `userData.crest`, see shell/chars.js
 * `addCrest`) are hidden, not removed; `dispose()` restores them. Call after
 * every `dress()`; call `update()` once per frame only if a character can be
 * hidden (it hides that character's crests). Opt-in — nothing else changes.
 *
 * @param {THREE.Object3D[]} chars  toy-rig characters (others are skipped)
 * @returns {{mesh: THREE.SkinnedMesh|null, update: () => void, dispose: () => void}}
 */
export function batchCrests(chars) {
  const entries = [];
  for (const c of chars) {
    if (!c?.joints) continue;
    c.traverse((o) => { if (o.isMesh && o.userData.crest && o.visible) entries.push({ c, crest: o }); });
  }
  if (!entries.length) return { mesh: null, update() {}, dispose() {} };

  const parts = [];
  const bones = [];
  entries.forEach((e, i) => {
    const src = e.crest.geometry.index ? e.crest.geometry.toNonIndexed() : e.crest.geometry.clone();
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', src.getAttribute('position'));
    g.setAttribute('normal', src.getAttribute('normal'));
    const n = g.getAttribute('position').count;
    const col = new Float32Array(n * 3);
    const { r, g: gg, b } = e.crest.material.color;   // already linear
    for (let k = 0; k < n; k++) { col[k * 3] = r; col[k * 3 + 1] = gg; col[k * 3 + 2] = b; }
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    const idx = new Uint16Array(n * 4);
    const wt = new Float32Array(n * 4);
    for (let k = 0; k < n; k++) { idx[k * 4] = i; wt[k * 4] = 1; }
    g.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(idx, 4));
    g.setAttribute('skinWeight', new THREE.Float32BufferAttribute(wt, 4));
    parts.push(g);
    if (e.crest.material.side === THREE.DoubleSide) parts.push(backFaces(g));
    src.dispose();

    const bone = new THREE.Bone();
    bone.name = 'crestBone';
    bone.position.copy(e.crest.position);
    bone.quaternion.copy(e.crest.quaternion);
    bone.scale.copy(e.crest.scale);
    e.crest.parent.add(bone);
    bones.push(bone);
    e.bone = bone;
    e.shown = true;
    e.crest.visible = false;
  });

  const geo = mergeGeometries(parts, false);
  for (const p of parts) p.dispose();
  if (!geo) throw new Error('batchCrests: crest geometries did not merge');
  // Identity inverses and bind matrix: each vertex lands at bone.matrixWorld
  // * its crest-local position, wherever the batch mesh itself is parented.
  const skeleton = new THREE.Skeleton(bones, bones.map(() => new THREE.Matrix4()));
  const material = new THREE.MeshStandardMaterial({ color: 0xffffff, vertexColors: true, roughness: 0.4 });
  const mesh = new THREE.SkinnedMesh(geo, material);
  mesh.bind(skeleton, new THREE.Matrix4());
  mesh.name = 'crestBatch';
  mesh.frustumCulled = false;
  mesh.castShadow = entries.some((e) => e.crest.castShadow);

  function update() {
    for (const e of entries) {
      const show = e.c.visible && !!e.c.parent;
      if (show === e.shown) continue;
      e.shown = show;
      if (show) e.bone.scale.copy(e.crest.scale); else e.bone.scale.setScalar(0);
    }
  }

  function dispose() {
    mesh.removeFromParent();
    for (const e of entries) { e.bone.removeFromParent(); e.crest.visible = true; }
    skeleton.dispose();
    geo.dispose();
    mesh.material.dispose();   // may be the house dress pass's replacement
  }

  return { mesh, update, dispose };
}

/** A copy of a non-indexed triangle soup facing the other way. */
function backFaces(g) {
  const out = new THREE.BufferGeometry();
  for (const name of ['position', 'normal', 'color']) {
    const a = g.getAttribute(name);
    const arr = new Float32Array(a.array.length);
    for (let t = 0; t < a.count; t += 3) {
      // swap vertices 1 and 2 of each triangle to flip the winding
      for (const [d, s] of [[t, t], [t + 1, t + 2], [t + 2, t + 1]]) {
        for (let c = 0; c < 3; c++) arr[d * 3 + c] = a.array[s * 3 + c] * (name === 'normal' ? -1 : 1);
      }
    }
    out.setAttribute(name, new THREE.BufferAttribute(arr, 3));
  }
  // one bone per crest: the skin attributes are constant, so share them
  out.setAttribute('skinIndex', g.getAttribute('skinIndex'));
  out.setAttribute('skinWeight', g.getAttribute('skinWeight'));
  return out;
}
