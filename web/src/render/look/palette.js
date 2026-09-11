/**
 * Palettes — the colour half of the house style.  [render agent owns this dir]
 *
 * One named palette per minigame plus the shell. A palette is not "some
 * colours"; it is a complete description of a lit world: what the sky does
 * from zenith to horizon, what the ground is, which hue carries gameplay
 * meaning, what the rim light separates silhouettes with, where the fog sits,
 * and how the verdict colours are re-tinted so they still read on THAT sky.
 *
 * Two rules make the set feel like one product:
 *
 *  1. Backgrounds are mid-to-dark and desaturated relative to the props. Every
 *     palette leaves the top of the value range free for gameplay-critical
 *     objects, so the bright, saturated things are always the things you must
 *     look at.
 *  2. Contrast is checked, not vibed. `auditPalette` measures WCAG relative
 *     luminance of every gameplay colour against the background behind it and
 *     pushes any colour that lands within MIN_LUMA_DELTA of its backdrop out
 *     of that band. A palette that reads badly cannot be shipped by accident.
 *
 * Palettes interpolate: `PaletteState` cross-fades every channel so a scene
 * change is a lighting change, not a cut.
 */

import * as THREE from 'three';
import { FEEL } from '../../core/feel.js';
import { clamp01, smootherstep } from '../../core/util.js';

/** Gameplay colours must differ from their backdrop by at least this much
 *  relative luminance (0..1). ~15% is the brief; we aim slightly over. */
export const MIN_LUMA_DELTA = 0.17;

// ---------------------------------------------------------------- colour math

const _c = new THREE.Color();
const _hsl = { h: 0, s: 0, l: 0 };

const srgbToLin = (v) => (v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));

/** WCAG relative luminance of an sRGB hex. */
export function luminance(hex) {
  const r = ((hex >> 16) & 255) / 255;
  const g = ((hex >> 8) & 255) / 255;
  const b = (hex & 255) / 255;
  return 0.2126 * srgbToLin(r) + 0.7152 * srgbToLin(g) + 0.0722 * srgbToLin(b);
}

/** Absolute luminance separation of two sRGB hexes, 0..1. */
export const lumaDelta = (a, b) => Math.abs(luminance(a) - luminance(b));

function hexToHsl(hex) {
  _c.setHex(hex, THREE.SRGBColorSpace);
  _c.getHSL(_hsl, THREE.SRGBColorSpace);
  return { h: _hsl.h, s: _hsl.s, l: _hsl.l };
}

function hslToHex(h, s, l) {
  _c.setHSL(h, clamp01(s), clamp01(l), THREE.SRGBColorSpace);
  return _c.getHex(THREE.SRGBColorSpace);
}

/**
 * Push `fg` away from `bg` in luminance until they are at least `min` apart,
 * preserving hue. Saturation is nudged up as lightness moves so the colour
 * gets *brighter*, not *paler* — a washed-out gameplay colour is its own bug.
 */
export function ensureContrast(fg, bg, min = MIN_LUMA_DELTA) {
  if (lumaDelta(fg, bg) >= min) return fg;
  const { h, s, l } = hexToHsl(fg);
  const up = luminance(fg) >= luminance(bg);
  let out = fg;
  for (let i = 1; i <= 14; i++) {
    const step = i * 0.045;
    const nl = clamp01(up ? l + step : l - step);
    const ns = clamp01(s + (up ? step * 0.35 : step * 0.15));
    out = hslToHex(h, ns, nl);
    if (lumaDelta(out, bg) >= min) return out;
    if ((up && nl >= 1) || (!up && nl <= 0)) break;
  }
  // Ran out of headroom in that direction — go the other way instead.
  for (let i = 1; i <= 14; i++) {
    const step = i * 0.05;
    const nl = clamp01(up ? l - step : l + step);
    out = hslToHex(h, clamp01(s + step * 0.2), nl);
    if (lumaDelta(out, bg) >= min) return out;
  }
  return out;
}

/** Mix two sRGB hexes in linear space. */
export function mixHex(a, b, t) {
  const ca = new THREE.Color().setHex(a, THREE.SRGBColorSpace);
  const cb = new THREE.Color().setHex(b, THREE.SRGBColorSpace);
  return ca.lerp(cb, clamp01(t)).getHex(THREE.SRGBColorSpace);
}

// ------------------------------------------------------------------ palettes

/**
 * @typedef {Object} Palette
 * Colours are sRGB hex. Directions are world-space [x,y,z].
 */

const BASE = {
  // sky
  skyTop: 0x1a1140, skyMid: 0x3b2470, skyBot: 0xff8f5e,
  sunDir: [-0.25, 0.12, -1], sun: 0xffd6a0, sunSize: 0.22, sunStrength: 0.75,
  // ground / set dressing
  ground: 0x2c2154, groundAlt: 0x3a2c6b, groundRim: 0xffd93d,
  band: 0x4a3382, bandAlt: 0x6a45a8,
  // gameplay hues
  accent: 0xffd93d, accent2: 0x4dd6ff, accent3: 0xff5d73,
  crowd: [0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a, 0xffffff],
  // lighting
  key: 0xfff2dc, keyDir: [-0.55, 0.78, 0.42], keyIntensity: 2.5,
  fillSky: 0x9fb8ff, fillGround: 0x3b1f52, fillIntensity: 1.15,
  rim: 0x8fe8ff, rimDir: [0.62, 0.35, -0.7], rimStrength: 0.85, rimPower: 2.6,
  // atmosphere
  fog: 0x3b2470, fogNear: 16, fogFar: 62,
  // grade
  exposure: 1.05, bloom: 0.85, bloomThreshold: 0.72, saturation: 1.08,
  vignette: 0.42,
  // verdicts (defaults come from feel.js; per-palette overrides re-tint them)
  verdict: { ...FEEL.color },
};

function P(over) {
  const p = { ...BASE, ...over };
  p.verdict = { ...BASE.verdict, ...(over.verdict || {}) };
  p.crowd = over.crowd || BASE.crowd.slice();
  return p;
}

/** The registry. `stage.setPalette(name)` takes any key here. */
export const PALETTES = {
  /** Shell: dusk fairground. Warm horizon, cool zenith, gold furniture. */
  shell: P({
    skyTop: 0x140b38, skyMid: 0x3d1f6e, skyBot: 0xff7a4d,
    sun: 0xffc06a, sunDir: [0, -0.05, -1], sunStrength: 0.9, sunSize: 0.3,
    ground: 0x241a4e, groundAlt: 0x33265f, groundRim: 0xffd93d,
    band: 0x4a2a80, bandAlt: 0x7a3fb0,
    accent: 0xffd93d, accent2: 0x59e2ff, accent3: 0xff6bd6,
    rim: 0xff8fd8, rimDir: [0.5, 0.3, -0.8], rimStrength: 0.9,
    fillSky: 0x8ea6ff, fillGround: 0x4a2050,
    fog: 0x3a1f68, fogNear: 18, fogFar: 70,
    bloom: 0.95,
  }),

};

/** Swing Kings: floodlit dusk ballpark. Turf green, sodium-lamp key. */
PALETTES['swing-kings'] = P({
  skyTop: 0x08163f, skyMid: 0x1d3f86, skyBot: 0xff9a5c,
  sun: 0xffcf8a, sunDir: [0.35, -0.02, -1], sunStrength: 0.85, sunSize: 0.26,
  ground: 0x1c6b45, groundAlt: 0x238055, groundRim: 0xfff0a8,
  band: 0x14336e, bandAlt: 0x2a5aa8,
  accent: 0xffd93d, accent2: 0x5ce1ff, accent3: 0xff6b5d,
  crowd: [0xffd93d, 0x5ce1ff, 0xff6b5d, 0xffffff, 0x9ee87a],
  key: 0xfff6e0, keyDir: [-0.5, 0.82, 0.35], keyIntensity: 2.7,
  fillSky: 0x9dc6ff, fillGround: 0x123f2c, fillIntensity: 1.2,
  rim: 0xffe9a8, rimDir: [0.6, 0.3, -0.75], rimStrength: 0.95,
  fog: 0x1b3a72, fogNear: 20, fogFar: 78,
  bloom: 0.8, exposure: 1.08,
});

/**
 * Swing Kings, late innings: the sun is gone and the stadium lights carry the
 * frame. The chart's fast off-beat section cross-fades into this, so the
 * stage escalates with the music instead of looking the same for 70 seconds.
 */
PALETTES['swing-kings-night'] = P({
  skyTop: 0x040a24, skyMid: 0x0f2258, skyBot: 0x5b3a8c,
  sun: 0xb8d4ff, sunDir: [0.35, 0.1, -1], sunStrength: 0.45, sunSize: 0.18,
  // Rich, not milky: a warm floodlight key over a dim blue fill keeps the
  // turf green and the dirt warm (a bright blue fill washed both to teal/grey).
  ground: 0x176a3a, groundAlt: 0x1d7a44, groundRim: 0xd8f0ff,
  band: 0x0e2458, bandAlt: 0x2346a0,
  accent: 0xffe066, accent2: 0x7fe7ff, accent3: 0xff5d7a,
  crowd: [0xffe066, 0x7fe7ff, 0xff5d7a, 0xffffff, 0xb28cff],
  key: 0xfff0d6, keyDir: [-0.35, 0.9, 0.3], keyIntensity: 3.0,
  fillSky: 0x3f5aa6, fillGround: 0x0b2a1c, fillIntensity: 0.72,
  rim: 0xbfe6ff, rimDir: [0.6, 0.35, -0.75], rimStrength: 1.1,
  fog: 0x0a1638, fogNear: 22, fogFar: 80,
  bloom: 0.9, exposure: 1.0,
});

/** Drumline Dash: hot night parade. Magenta and ember. */
PALETTES['drumline-dash'] = P({
  skyTop: 0x1e0630, skyMid: 0x6a1055, skyBot: 0xff5a3c,
  sun: 0xff9a4a, sunDir: [-0.4, -0.06, -1], sunStrength: 0.95, sunSize: 0.28,
  ground: 0x2c0c3e, groundAlt: 0x3d1252, groundRim: 0xffb03a,
  band: 0x5c1160, bandAlt: 0x94207a,
  accent: 0xffb03a, accent2: 0xff5fd0, accent3: 0x5ce1ff,
  crowd: [0xffb03a, 0xff5fd0, 0x5ce1ff, 0xffffff, 0xffd93d],
  key: 0xffe7cf, keyDir: [-0.6, 0.75, 0.4], keyIntensity: 2.6,
  fillSky: 0xff9ad8, fillGround: 0x3a0d40, fillIntensity: 1.1,
  rim: 0xff6bd6, rimStrength: 1.0,
  fog: 0x5a1050, fogNear: 15, fogFar: 60,
  bloom: 1.0,
});

/** Bounce Brigade: bright poolside afternoon, cool and clean. */
PALETTES['bounce-brigade'] = P({
  skyTop: 0x042a52, skyMid: 0x0d6f9e, skyBot: 0x5fe0d0,
  sun: 0xbdfff2, sunDir: [0.2, 0.05, -1], sunStrength: 0.7, sunSize: 0.24,
  ground: 0x0b4a63, groundAlt: 0x0f5f7c, groundRim: 0x9ee87a,
  band: 0x0d5f8a, bandAlt: 0x1a86b8,
  accent: 0x9ee87a, accent2: 0xffd93d, accent3: 0xff7ad0,
  crowd: [0x9ee87a, 0xffd93d, 0xff7ad0, 0xffffff, 0x7ef7ff],
  key: 0xf2fbff, keyDir: [-0.45, 0.8, 0.4], keyIntensity: 2.6,
  fillSky: 0x9ef0ff, fillGround: 0x0a3d52, fillIntensity: 1.25,
  rim: 0x7ef7ff, rimStrength: 0.85,
  fog: 0x0d6489, fogNear: 20, fogFar: 76,
  bloom: 0.75, saturation: 1.1,
});

/** Chomp Chorus: purple midnight kitchen, acid-lime highlights. */
PALETTES['chomp-chorus'] = P({
  skyTop: 0x110226, skyMid: 0x3f0e5e, skyBot: 0xb03bb0,
  sun: 0xff9ae0, sunDir: [0.1, -0.08, -1], sunStrength: 0.8, sunSize: 0.3,
  ground: 0x220a3a, groundAlt: 0x31104f, groundRim: 0x9ee87a,
  band: 0x45126a, bandAlt: 0x6d1f9c,
  accent: 0x9ee87a, accent2: 0xff5d73, accent3: 0x6fd0ff,
  crowd: [0x9ee87a, 0xff5d73, 0x6fd0ff, 0xffd93d, 0xffffff],
  key: 0xf0e2ff, keyDir: [-0.5, 0.78, 0.38], keyIntensity: 2.4,
  fillSky: 0xb98cff, fillGround: 0x2a0b40, fillIntensity: 1.05,
  rim: 0x9ee87a, rimStrength: 1.0, rimPower: 2.2,
  fog: 0x3a0d58, fogNear: 14, fogFar: 58,
  bloom: 1.0,
});

/** Finale Fever: crimson and gold, fireworks over a dark house. */
PALETTES['finale-fever'] = P({
  skyTop: 0x1c0418, skyMid: 0x74102c, skyBot: 0xff7a2f,
  sun: 0xffb45a, sunDir: [0, 0.0, -1], sunStrength: 1.0, sunSize: 0.34,
  ground: 0x2c0820, groundAlt: 0x3f0c2c, groundRim: 0xffd93d,
  band: 0x5e0d28, bandAlt: 0x9c1a38,
  accent: 0xffd93d, accent2: 0xff8a3c, accent3: 0x6fd0ff,
  crowd: [0xffd93d, 0xff8a3c, 0x6fd0ff, 0xffffff, 0xff5d73],
  key: 0xfff0d8, keyDir: [-0.55, 0.76, 0.42], keyIntensity: 2.7,
  fillSky: 0xffa9a0, fillGround: 0x360a24, fillIntensity: 1.1,
  rim: 0xffb45a, rimStrength: 1.05,
  fog: 0x63102a, fogNear: 14, fogFar: 64,
  bloom: 1.1, exposure: 1.1,
});

/** Neutral fallback for anything unregistered. */
PALETTES.default = P({});

// ------------------------------------------------------------------- audit

/**
 * Measure every gameplay colour against the backgrounds it can appear over,
 * and fix the ones that fail. Returns the report so a dev overlay (or a test)
 * can show what moved. Mutates the palette — a fixed palette is the shipped
 * palette.
 *
 * "Behind it" is either the sky (props in the air, verdict popups, characters
 * above the horizon) or the ground (anything resting on the stage), so each
 * colour is checked against the worse of the two.
 */
export function auditPalette(p) {
  const report = [];
  const backs = [
    ['skyMid', p.skyMid], ['skyTop', p.skyTop], ['ground', p.ground], ['band', p.band],
  ];
  const check = (key, get, set) => {
    let v = get();
    for (const [bn, bg] of backs) {
      const fixed = ensureContrast(v, bg);
      if (fixed !== v) {
        report.push({ key, against: bn, from: v, to: fixed, delta: lumaDelta(v, bg) });
        v = fixed;
      }
    }
    if (v !== get()) set(v);
  };

  check('accent', () => p.accent, (v) => { p.accent = v; });
  check('accent2', () => p.accent2, (v) => { p.accent2 = v; });
  check('accent3', () => p.accent3, (v) => { p.accent3 = v; });
  check('groundRim', () => p.groundRim, (v) => { p.groundRim = v; });
  for (const k of Object.keys(p.verdict)) {
    if (k === 'bgDeep') continue;
    check('verdict.' + k, () => p.verdict[k], (v) => { p.verdict[k] = v; });
  }
  p._audit = report;
  return report;
}

for (const k of Object.keys(PALETTES)) auditPalette(PALETTES[k]);

export function getPalette(name) {
  return PALETTES[name] || PALETTES.default;
}

/** Register (or override) a palette at runtime; audited on the way in. */
export function definePalette(name, spec) {
  const p = P(spec);
  auditPalette(p);
  PALETTES[name] = p;
  return p;
}

// ------------------------------------------------------------- live palette

/**
 * A palette being *used*: holds THREE.Colors that everything (sky shader,
 * lights, fog, materials, post) reads every frame, and cross-fades them on a
 * palette change so scene transitions are lit, not cut.
 */
export class PaletteState {
  constructor(name = 'default') {
    const p = getPalette(name);
    this.name = name;
    this.from = p;
    this.to = p;
    this.t = 1;
    this.dur = 0.001;
    /** Scalars, live. */
    this.num = {};
    /** THREE.Colors, live. Read these; never replace them. */
    this.col = {};
    this.dir = {};
    this.verdict = {};
    this.crowd = [];
    for (const k of COLOR_KEYS) this.col[k] = new THREE.Color();
    for (const k of NUM_KEYS) this.num[k] = 0;
    for (const k of DIR_KEYS) this.dir[k] = new THREE.Vector3();
    for (const k of Object.keys(p.verdict)) this.verdict[k] = new THREE.Color();
    this._apply(1);
    this.onChange = null;
  }

  /** Colour of a verdict as a css string, for UI handoff. */
  verdictHex(v) {
    const c = this.verdict[v] || this.verdict.perfect;
    return '#' + c.getHexString(THREE.SRGBColorSpace);
  }

  /** Current palette spec (target). Read-only. */
  get spec() { return this.to; }

  set(name, dur = 0.55) {
    const p = getPalette(name);
    if (p === this.to && this.t >= 1) { this.name = name; return; }
    this.from = this._snapshot();
    this.to = p;
    this.name = name;
    this.t = 0;
    this.dur = Math.max(0.001, dur);
    if (dur <= 0.001) this._apply(1);
  }

  /** Freeze the current interpolated values as a palette-shaped object. */
  _snapshot() {
    const s = { verdict: {}, crowd: this.to.crowd };
    for (const k of COLOR_KEYS) s[k] = this.col[k].getHex(THREE.SRGBColorSpace);
    for (const k of NUM_KEYS) s[k] = this.num[k];
    for (const k of DIR_KEYS) s[k] = [this.dir[k].x, this.dir[k].y, this.dir[k].z];
    for (const k of Object.keys(this.verdict)) s.verdict[k] = this.verdict[k].getHex(THREE.SRGBColorSpace);
    return s;
  }

  update(dt) {
    if (this.t >= 1) return false;
    this.t = clamp01(this.t + dt / this.dur);
    this._apply(smootherstep(this.t));
    if (this.onChange) this.onChange(this.t >= 1);
    return true;
  }

  get transitioning() { return this.t < 1; }

  _apply(k) {
    const a = this.from, b = this.to;
    for (const key of COLOR_KEYS) {
      const ca = _tmpA.setHex(a[key] ?? BASE[key], THREE.SRGBColorSpace);
      const cb = _tmpB.setHex(b[key] ?? BASE[key], THREE.SRGBColorSpace);
      this.col[key].copy(ca).lerp(cb, k);
    }
    for (const key of NUM_KEYS) {
      const va = a[key] ?? BASE[key], vb = b[key] ?? BASE[key];
      this.num[key] = va + (vb - va) * k;
    }
    for (const key of DIR_KEYS) {
      const va = a[key] || BASE[key], vb = b[key] || BASE[key];
      this.dir[key].set(
        va[0] + (vb[0] - va[0]) * k,
        va[1] + (vb[1] - va[1]) * k,
        va[2] + (vb[2] - va[2]) * k
      ).normalize();
    }
    for (const key of Object.keys(this.verdict)) {
      const va = (a.verdict && a.verdict[key]) ?? BASE.verdict[key] ?? 0xffffff;
      const vb = (b.verdict && b.verdict[key]) ?? BASE.verdict[key] ?? 0xffffff;
      this.verdict[key].copy(_tmpA.setHex(va, THREE.SRGBColorSpace))
        .lerp(_tmpB.setHex(vb, THREE.SRGBColorSpace), k);
    }
    this.crowd = b.crowd;
  }
}

const _tmpA = new THREE.Color();
const _tmpB = new THREE.Color();

export const COLOR_KEYS = [
  'skyTop', 'skyMid', 'skyBot', 'sun',
  'ground', 'groundAlt', 'groundRim', 'band', 'bandAlt',
  'accent', 'accent2', 'accent3',
  'key', 'fillSky', 'fillGround', 'rim', 'fog',
];
export const NUM_KEYS = [
  'sunSize', 'sunStrength', 'keyIntensity', 'fillIntensity',
  'rimStrength', 'rimPower', 'fogNear', 'fogFar',
  'exposure', 'bloom', 'bloomThreshold', 'saturation', 'vignette',
];
export const DIR_KEYS = ['sunDir', 'keyDir', 'rimDir'];
