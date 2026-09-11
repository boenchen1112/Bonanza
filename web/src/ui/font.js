/**
 * BASH — a procedural display typeface.   [ui agent owns this dir]
 *
 * There is no network and there are no font files, so the game ships its own
 * display face: every glyph is defined here as vector geometry in an em box
 * and rasterised on demand.
 *
 * ## The design
 *
 * It is a *stroked* face, not an outline face. Each glyph is a skeleton of
 * lines, quadratics and elliptical arcs, drawn with a very heavy round-capped
 * pen. That buys three things an outline font would have cost a fortune in
 * hand-authored contours:
 *
 *  - the rounded, extruded "party game" silhouette comes free from `lineCap`
 *    and `lineJoin: round`;
 *  - the outline is just the same skeleton stroked wider underneath, so it is
 *    always perfectly parallel to the letterform;
 *  - weight, outline thickness and italic slant are numbers, so the whole
 *    family can be re-proportioned without redrawing a single glyph.
 *
 * The geometry constants are chosen against one hard constraint: the counters
 * (the holes in O, B, 8) must survive the *outline* pen, not just the fill
 * pen, or every round letter collapses into a blob. That is why the pen is
 * 0.185em rather than the 0.26em a solid display face would use — the visible
 * silhouette is 0.275em once outlined, which reads as heavy, while the counter
 * of an O stays 0.29em wide.
 *
 * ## Coordinate system
 *
 * x runs left to right from the glyph origin; the advance width is per glyph.
 * y is 0 at the cap line and 1 at the baseline (screen-style, y down), so cap
 * height is exactly 1em and everything scales by a single `capPx`.
 *
 * ## Rendering
 *
 * A string is rasterised once into a canvas and cached as a data URL, keyed by
 * text + style. The DOM then scales that image with CSS, so re-laying out on a
 * resize costs nothing and a popup that fires sixty times a round rasterises
 * once. Layers, back to front: hard drop shadow, dark outline, gradient fill,
 * and a `source-atop` sheen along the top of every stroke.
 */

const TAU = Math.PI * 2;

/** Fill pen width, em. */
export const PEN = 0.215;
/** Extra outline pen per side, em. */
export const OUTLINE = 0.055;
/** Half the widest pen — the inset every glyph must keep from its own box. */
const H = (PEN + 2 * OUTLINE) / 2; // 0.1375
/** How far below the baseline a descender (comma, Q tail, J) may reach. */
const DESC = 0.22;

/**
 * Glyph factory. `build` receives the glyph's drawing box so letterforms are
 * written in terms of edges rather than magic numbers.
 */
function G(w, build) {
  const L = H, R = w - H, C = w / 2, T = H, B = 1 - H;
  const k = {
    w, L, R, C, T, B,
    cx: (L + R) / 2, cy: 0.5,
    rx: (R - L) / 2, ry: (B - T) / 2,
  };
  return { w, cmds: build(k) };
}

/** Bowl of P/R/B — a rounded box, drawn open on the left so a stem closes it. */
function bowl(k, top, bottom, right) {
  const x1 = k.L + (right - k.L) * 0.34;
  return [
    ['M', k.L, top], ['L', x1, top],
    ['Q', right, top, right, (top + bottom) / 2],
    ['Q', right, bottom, x1, bottom],
    ['L', k.L, bottom],
  ];
}

/** A round dot (zero-length stroke with a round cap). */
const dot = (x, y) => [['M', x, y], ['L', x + 0.0008, y]];

export const GLYPHS = {
  A: G(0.90, (k) => {
    const y = 0.63;
    const x = k.L + (k.C - k.L) * (k.B - y) / (k.B - k.T);
    return [['M', k.L, k.B], ['L', k.C, k.T], ['L', k.R, k.B], ['M', x, y], ['L', k.w - x, y]];
  }),
  B: G(0.86, (k) => [
    ...bowl(k, k.T, 0.5, k.R - 0.04),
    ...bowl(k, 0.5, k.B, k.R),
    ['M', k.L, k.T], ['L', k.L, k.B],
  ]),
  C: G(0.88, (k) => [['E', k.cx, k.cy, k.rx, k.ry, TAU * 0.10, TAU * 0.90, false]]),
  D: G(0.90, (k) => [
    ['M', k.L, k.T], ['L', k.L + 0.15, k.T],
    ['Q', k.R, k.T, k.R, k.cy], ['Q', k.R, k.B, k.L + 0.15, k.B],
    ['L', k.L, k.B], ['L', k.L, k.T],
  ]),
  E: G(0.80, (k) => [
    ['M', k.R, k.T], ['L', k.L, k.T], ['L', k.L, k.B], ['L', k.R, k.B],
    ['M', k.L, 0.5], ['L', k.R - 0.08, 0.5],
  ]),
  F: G(0.76, (k) => [
    ['M', k.R, k.T], ['L', k.L, k.T], ['L', k.L, k.B],
    ['M', k.L, 0.5], ['L', k.R - 0.06, 0.5],
  ]),
  // Classic geometric G: the ring breaks at the lower right, the terminal
  // drops to the middle as a short stem, and the spur points back inwards.
  // A C, with the spur hung off its LOWER arm. Hang it off the upper arm
  // instead and the glyph reads as a lowercase e, which is how most naive
  // geometric Gs go wrong.
  G: G(0.98, (k) => {
    const a = TAU * 0.07;
    const sx = k.cx + k.rx * Math.cos(a);
    return [
      ['E', k.cx, k.cy, k.rx, k.ry, a, TAU * 0.87, false],
      ['M', sx, k.cy + k.ry * Math.sin(a)],
      ['L', sx, k.cy], ['L', k.cx + 0.05, k.cy],
    ];
  }),
  H: G(0.90, (k) => [
    ['M', k.L, k.T], ['L', k.L, k.B], ['M', k.R, k.T], ['L', k.R, k.B],
    ['M', k.L, 0.5], ['L', k.R, 0.5],
  ]),
  I: G(0.42, (k) => [['M', k.C, k.T], ['L', k.C, k.B]]),
  J: G(0.76, (k) => [
    ['M', k.R, k.T], ['L', k.R, k.B - 0.16],
    ['Q', k.R, k.B, k.C - 0.02, k.B], ['Q', k.L, k.B, k.L, k.B - 0.20],
  ]),
  K: G(0.88, (k) => [
    ['M', k.L, k.T], ['L', k.L, k.B],
    ['M', k.R, k.T], ['L', k.L + 0.11, 0.55], ['L', k.R, k.B],
  ]),
  L: G(0.74, (k) => [['M', k.L, k.T], ['L', k.L, k.B], ['L', k.R, k.B]]),
  M: G(1.10, (k) => [
    ['M', k.L, k.B], ['L', k.L, k.T], ['L', k.C, 0.47], ['L', k.R, k.T], ['L', k.R, k.B],
  ]),
  N: G(0.94, (k) => [['M', k.L, k.B], ['L', k.L, k.T], ['L', k.R, k.B], ['L', k.R, k.T]]),
  O: G(0.96, (k) => [['E', k.cx, k.cy, k.rx, k.ry, 0, TAU, false]]),
  P: G(0.86, (k) => [...bowl(k, k.T, 0.56, k.R), ['M', k.L, k.T], ['L', k.L, k.B]]),
  Q: G(0.96, (k) => [
    ['E', k.cx, k.cy, k.rx, k.ry, 0, TAU, false],
    ['M', k.cx + 0.05, k.cy + 0.14], ['L', k.R + 0.02, k.B + 0.13],
  ]),
  R: G(0.90, (k) => [
    ...bowl(k, k.T, 0.54, k.R - 0.03),
    ['M', k.L, k.T], ['L', k.L, k.B],
    ['M', k.L + 0.20, 0.54], ['L', k.R, k.B],
  ]),
  S: G(0.82, (k) => {
    const ry = (k.B - k.T) / 4;
    return [
      ['E', k.cx, k.T + ry, k.rx, ry, TAU * 0.94, TAU * 0.25, true],
      ['E', k.cx, k.B - ry, k.rx, ry, TAU * 0.75, TAU * 0.44, false],
    ];
  }),
  T: G(0.82, (k) => [['M', k.L, k.T], ['L', k.R, k.T], ['M', k.C, k.T], ['L', k.C, k.B]]),
  U: G(0.92, (k) => {
    const r = k.rx;
    return [['M', k.L, k.T], ['L', k.L, k.B - r], ['E', k.cx, k.B - r, r, r, Math.PI, 0, true], ['L', k.R, k.T]];
  }),
  V: G(0.92, (k) => [['M', k.L, k.T], ['L', k.C, k.B], ['L', k.R, k.T]]),
  W: G(1.28, (k) => {
    const d = k.R - k.L;
    return [
      ['M', k.L, k.T], ['L', k.L + d * 0.25, k.B], ['L', k.C, k.T + 0.32],
      ['L', k.R - d * 0.25, k.B], ['L', k.R, k.T],
    ];
  }),
  X: G(0.90, (k) => [['M', k.L, k.T], ['L', k.R, k.B], ['M', k.R, k.T], ['L', k.L, k.B]]),
  Y: G(0.88, (k) => [
    ['M', k.L, k.T], ['L', k.C, 0.53], ['L', k.R, k.T], ['M', k.C, 0.53], ['L', k.C, k.B],
  ]),
  Z: G(0.84, (k) => [['M', k.L, k.T], ['L', k.R, k.T], ['L', k.L, k.B], ['L', k.R, k.B]]),

  // --- figures. One advance for all ten, so numbers never wobble. ---------
  0: G(0.80, (k) => [['E', k.cx, k.cy, k.rx, k.ry, 0, TAU, false]]),
  1: G(0.80, (k) => [['M', k.C - 0.17, k.T + 0.17], ['L', k.C, k.T], ['L', k.C, k.B]]),
  2: G(0.80, (k) => {
    const r = k.rx, cy = k.T + r * 0.96;
    return [
      ['E', k.cx, cy, r, r * 0.96, Math.PI * 0.92, TAU, false],
      ['L', k.L, k.B], ['L', k.R, k.B],
    ];
  }),
  3: G(0.80, (k) => {
    const ry = (k.B - k.T) / 4, rx = k.rx;
    return [
      ['E', k.cx, k.T + ry, rx, ry, Math.PI * 1.16, Math.PI * 0.5 + TAU, false],
      ['E', k.cx, k.B - ry, rx, ry, -Math.PI * 0.5, Math.PI * 0.84, false],
    ];
  }),
  4: G(0.80, (k) => {
    const sx = k.R - 0.05, by = 0.68;
    return [['M', sx, k.T], ['L', k.L, by], ['L', k.R, by], ['M', sx, k.T], ['L', sx, k.B]];
  }),
  5: G(0.80, (k) => [
    ['M', k.R, k.T], ['L', k.L, k.T], ['L', k.L, 0.42], ['L', k.C - 0.02, 0.42],
    ['Q', k.R, 0.42, k.R, 0.64], ['Q', k.R, k.B, k.C - 0.04, k.B],
    ['Q', k.L, k.B, k.L, k.B - 0.16],
  ]),
  // 6 and 9: a long, nearly straight stem into the bowl. The old short curled
  // flick left a "6" that read as "c" or "ó" in the HUD score.
  6: G(0.80, (k) => {
    const r = k.rx, cyb = k.B - r;
    return [
      ['M', k.R - 0.06, k.T],
      ['Q', k.L + 0.02, k.T + 0.16, k.L, cyb],
      ['E', k.cx, cyb, r, r, Math.PI, Math.PI + TAU, false],
    ];
  }),
  7: G(0.80, (k) => [['M', k.L, k.T], ['L', k.R, k.T], ['L', k.C - 0.03, k.B]]),
  8: G(0.80, (k) => {
    const ry = (k.B - k.T) / 4;
    return [
      ['E', k.cx, k.T + ry, k.rx * 0.82, ry, 0, TAU, false],
      ['E', k.cx, k.B - ry, k.rx, ry, 0, TAU, false],
    ];
  }),
  9: G(0.80, (k) => {
    const r = k.rx, cyt = k.T + r;
    return [
      ['M', k.L + 0.06, k.B],
      ['Q', k.R - 0.02, k.B - 0.16, k.R, cyt],
      ['E', k.cx, cyt, r, r, 0, TAU, false],
    ];
  }),

  // --- punctuation and marks ---------------------------------------------
  ' ': G(0.34, () => []),
  '!': G(0.42, (k) => [['M', k.C, k.T], ['L', k.C, k.B - 0.24], ...dot(k.C, k.B)]),
  '?': G(0.78, (k) => {
    const r = k.rx, cy = k.T + r * 0.92;
    return [
      ['E', k.cx, cy, r, r * 0.92, Math.PI * 0.98, TAU * 0.08, false],
      ['Q', k.C + 0.10, 0.52, k.C, k.B - 0.26],
      ...dot(k.C, k.B),
    ];
  }),
  '.': G(0.40, (k) => dot(k.C, k.B)),
  ',': G(0.40, (k) => [['M', k.C, k.B - 0.05], ['Q', k.C + 0.02, k.B + 0.06, k.C - 0.09, k.B + 0.17]]),
  ':': G(0.40, (k) => [...dot(k.C, 0.36), ...dot(k.C, k.B)]),
  ';': G(0.40, (k) => [...dot(k.C, 0.36), ['M', k.C, k.B - 0.05], ['Q', k.C + 0.02, k.B + 0.06, k.C - 0.09, k.B + 0.17]]),
  '-': G(0.62, (k) => [['M', k.L, 0.52], ['L', k.R, 0.52]]),
  '_': G(0.72, (k) => [['M', k.L, k.B + 0.10], ['L', k.R, k.B + 0.10]]),
  '+': G(0.76, (k) => [['M', k.L, 0.52], ['L', k.R, 0.52], ['M', k.C, 0.28], ['L', k.C, 0.76]]),
  '=': G(0.76, (k) => [['M', k.L, 0.38], ['L', k.R, 0.38], ['M', k.L, 0.66], ['L', k.R, 0.66]]),
  '/': G(0.64, (k) => [['M', k.L, k.B], ['L', k.R, k.T]]),
  '\\': G(0.64, (k) => [['M', k.L, k.T], ['L', k.R, k.B]]),
  '×': G(0.66, (k) => [['M', k.L, 0.40], ['L', k.R, k.B], ['M', k.R, 0.40], ['L', k.L, k.B]]),
  '*': G(0.62, (k) => [
    ['M', k.C, k.T], ['L', k.C, k.T + 0.34],
    ['M', k.C - 0.16, k.T + 0.06], ['L', k.C + 0.16, k.T + 0.26],
    ['M', k.C + 0.16, k.T + 0.06], ['L', k.C - 0.16, k.T + 0.26],
  ]),
  '%': G(1.06, (k) => {
    const r = 0.135;
    return [
      ['E', k.L + r, k.T + r, r, r, 0, TAU, false],
      ['E', k.R - r, k.B - r, r, r, 0, TAU, false],
      ['M', k.L + 0.06, k.B], ['L', k.R - 0.06, k.T],
    ];
  }),
  "'": G(0.36, (k) => [['M', k.C, k.T], ['L', k.C, k.T + 0.22]]),
  '"': G(0.56, (k) => [
    ['M', k.C - 0.11, k.T], ['L', k.C - 0.11, k.T + 0.22],
    ['M', k.C + 0.11, k.T], ['L', k.C + 0.11, k.T + 0.22],
  ]),
  '(': G(0.52, (k) => [['M', k.R, k.T - 0.03], ['Q', k.L - 0.06, 0.5, k.R, k.B + 0.03]]),
  ')': G(0.52, (k) => [['M', k.L, k.T - 0.03], ['Q', k.R + 0.06, 0.5, k.L, k.B + 0.03]]),
  '[': G(0.50, (k) => [['M', k.R, k.T - 0.03], ['L', k.L, k.T - 0.03], ['L', k.L, k.B + 0.03], ['L', k.R, k.B + 0.03]]),
  ']': G(0.50, (k) => [['M', k.L, k.T - 0.03], ['L', k.R, k.T - 0.03], ['L', k.R, k.B + 0.03], ['L', k.L, k.B + 0.03]]),
  '#': G(0.94, (k) => [
    ['M', k.L + 0.16, k.T], ['L', k.L + 0.04, k.B],
    ['M', k.R - 0.04, k.T], ['L', k.R - 0.16, k.B],
    ['M', k.L, 0.38], ['L', k.R, 0.38], ['M', k.L, 0.68], ['L', k.R, 0.68],
  ]),
  '→': G(1.00, (k) => [
    ['M', k.L, 0.5], ['L', k.R, 0.5],
    ['M', k.R - 0.22, 0.5 - 0.20], ['L', k.R, 0.5], ['L', k.R - 0.22, 0.5 + 0.20],
  ]),
  '←': G(1.00, (k) => [
    ['M', k.R, 0.5], ['L', k.L, 0.5],
    ['M', k.L + 0.22, 0.5 - 0.20], ['L', k.L, 0.5], ['L', k.L + 0.22, 0.5 + 0.20],
  ]),
  '↑': G(0.90, (k) => [
    ['M', k.C, k.B], ['L', k.C, k.T],
    ['M', k.C - 0.20, k.T + 0.22], ['L', k.C, k.T], ['L', k.C + 0.20, k.T + 0.22],
  ]),
  '↓': G(0.90, (k) => [
    ['M', k.C, k.T], ['L', k.C, k.B],
    ['M', k.C - 0.20, k.B - 0.22], ['L', k.C, k.B], ['L', k.C + 0.20, k.B - 0.22],
  ]),
};

// --------------------------------------------------------------- path cache

/** @type {Map<string, Path2D>} */
const pathCache = new Map();

function pathFor(ch) {
  let p = pathCache.get(ch);
  if (p !== undefined) return p;
  const g = GLYPHS[ch];
  if (!g) { pathCache.set(ch, null); return null; }
  p = new Path2D();
  for (const c of g.cmds) {
    switch (c[0]) {
      case 'M': p.moveTo(c[1], c[2]); break;
      case 'L': p.lineTo(c[1], c[2]); break;
      case 'Q': p.quadraticCurveTo(c[1], c[2], c[3], c[4]); break;
      case 'C': p.bezierCurveTo(c[1], c[2], c[3], c[4], c[5], c[6]); break;
      case 'E': p.ellipse(c[1], c[2], c[3], c[4], 0, c[5], c[6], c[7]); break;
      default: break;
    }
  }
  pathCache.set(ch, p);
  return p;
}

/** Resolve a character to a drawable glyph, folding case and a few aliases. */
function resolve(ch) {
  if (GLYPHS[ch]) return ch;
  const up = ch.toUpperCase();
  if (GLYPHS[up]) return up;
  if (ch === 'x' || ch === '·' || ch === '•') return '×';
  if (ch === '’') return "'";
  if (ch === '–' || ch === '—') return '-';
  return null;
}

// ------------------------------------------------------------------- colour

const hex = (n) => (typeof n === 'number' ? '#' + (n >>> 0).toString(16).padStart(6, '0') : n);

function rgb(c) {
  c = hex(c);
  if (c[0] !== '#') return [255, 255, 255];
  const s = c.length === 4
    ? c[1] + c[1] + c[2] + c[2] + c[3] + c[3]
    : c.slice(1, 7);
  const n = parseInt(s, 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));
const css = (c, a = 1) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;

// -------------------------------------------------------------------- style

/**
 * A style is a plain object so it can be cache-keyed cheaply.
 * @typedef {{
 *   capPx:number, tracking:number, slant:number, color:string|number,
 *   outline:string, shadow:[number,number], shadowColor:string,
 *   sheen:number, glow:number, pen:number, uppercase:boolean,
 * }} BashStyle
 */

/** @type {BashStyle} */
export const BASE_STYLE = {
  capPx: 128,
  tracking: 0.03,
  slant: 0.115,         // tan of the italic angle (~6.6 degrees)
  color: '#ffffff',
  outline: '#150f2c',
  shadow: [0.042, 0.10],
  shadowColor: 'rgba(6,4,18,0.72)',
  sheen: 0.42,
  glow: 0,
  pen: PEN,
  uppercase: true,
};

/** Named presets. Sizes are raster resolution, not layout size. */
export const STYLES = {
  /** Verdicts, banners, big numbers. */
  display: { ...BASE_STYLE, capPx: 150 },
  /** Titles — a touch more tracking so long words don't read as a wall. */
  title: { ...BASE_STYLE, capPx: 180, tracking: 0.045 },
  /** HUD readouts. Rasterised smaller; they never scale up much. */
  hud: { ...BASE_STYLE, capPx: 100, tracking: 0.02 },
  /** Small chips and pills. */
  chip: { ...BASE_STYLE, capPx: 72, tracking: 0.03, shadow: [0.02, 0.06] },
};

export function style(name, over) {
  const base = STYLES[name] || STYLES.display;
  return over ? { ...base, ...over } : base;
}

function keyOf(st) {
  return `${st.capPx}|${st.tracking}|${st.slant}|${hex(st.color)}|${st.outline}|${st.shadow}|`
    + `${st.shadowColor}|${st.sheen}|${st.glow}|${st.pen}|${st.uppercase}`;
}

// ---------------------------------------------------------------- rasterise

/** @type {Map<string, {url:string, w:number, h:number, capPx:number, baseline:number, ratio:number, aspect:number}>} */
const imgCache = new Map();
const CACHE_MAX = 320;

/** Advance width of a string, in em. */
export function advanceOf(text, st = STYLES.display) {
  const s = st.uppercase ? text.toUpperCase() : text;
  let a = 0;
  for (const ch of s) {
    const r = resolve(ch);
    a += (r ? GLYPHS[r].w : 0.34) + st.tracking;
  }
  return Math.max(0, a - st.tracking);
}

/** Lay a string out into positioned glyph references. */
function layout(text, st) {
  const src = st.uppercase ? text.toUpperCase() : text;
  const items = [];
  let adv = 0;
  for (const ch of src) {
    const r = resolve(ch);
    if (r && GLYPHS[r].cmds.length) items.push({ ch: r, x: adv });
    adv += (r ? GLYPHS[r].w : 0.34) + st.tracking;
  }
  return { items, adv: Math.max(0.001, adv - st.tracking) };
}

/** Padding, in em, that the layered treatment needs around the letterforms. */
function padOf(st) {
  const wide = st.pen + 2 * OUTLINE;
  const glowPad = st.glow ? st.glow * 1.2 : 0;
  return {
    wide,
    mx: wide / 2 + Math.abs(st.shadow[0]) + 0.05 + glowPad,
    my: wide / 2 + Math.abs(st.shadow[1]) + 0.05 + glowPad,
  };
}

/**
 * Paint the layered treatment for a laid-out string. Sets its own transform so
 * cap height 1em maps to `s` pixels, glyph origin at (ox, oy), baseline oy + s.
 */
function paint(g, items, st, s, ox, oy) {
  const { wide } = padOf(st);
  const pen = st.pen;
  const kx = st.slant;
  // x' = s*(x + (1-y)*kx) + ox ; y' = s*y + oy
  g.setTransform(s, 0, -s * kx, s, ox + s * kx, oy);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.miterLimit = 2;

  const strokeAll = () => {
    for (const it of items) {
      const p = pathFor(it.ch);
      if (!p) continue;
      g.save();
      g.translate(it.x, 0);
      g.stroke(p);
      g.restore();
    }
  };

  const base = rgb(st.color);
  const top = mix(base, [255, 255, 255], 0.34);
  const bot = mix(base, [0, 0, 0], 0.16);

  // 1. hard drop shadow: solid, offset, no blur. It is what pins the text to
  //    the screen instead of letting it float over the render.
  g.save();
  g.translate(st.shadow[0], st.shadow[1]);
  g.strokeStyle = st.shadowColor;
  g.lineWidth = wide;
  strokeAll();
  g.restore();

  // 2. outline
  if (st.glow) {
    g.shadowColor = css(base, 0.9);
    g.shadowBlur = st.glow * s;
  }
  g.strokeStyle = st.outline;
  g.lineWidth = wide;
  strokeAll();
  g.shadowBlur = 0;

  // 3. gradient fill
  const grad = g.createLinearGradient(0, -0.06, 0, 1.04);
  grad.addColorStop(0, css(top));
  grad.addColorStop(0.52, css(base));
  grad.addColorStop(1, css(bot));
  g.strokeStyle = grad;
  g.lineWidth = pen;
  strokeAll();

  // 4. sheen along the top of each stroke, clipped to what is already drawn.
  if (st.sheen > 0) {
    g.globalCompositeOperation = 'source-atop';
    g.save();
    g.translate(0, -pen * 0.30);
    g.strokeStyle = `rgba(255,255,255,${st.sheen})`;
    g.lineWidth = pen * 0.34;
    strokeAll();
    g.restore();
    g.globalCompositeOperation = 'source-over';
  }
  g.setTransform(1, 0, 0, 1, 0, 0);
}

/**
 * Rasterise a string into a cached image.
 * @returns {{url:string,w:number,h:number,capPx:number,baseline:number,ratio:number,aspect:number}}
 *  `ratio` is imageHeight / capHeight: multiply a desired cap size by it to get
 *  the CSS height the image must be drawn at.
 */
export function textImage(text, st = STYLES.display) {
  const key = keyOf(st) + ' ' + text;
  const hit = imgCache.get(key);
  if (hit) return hit;

  const { items, adv } = layout(text, st);
  const { mx, my } = padOf(st);
  const s = st.capPx;
  const cv = document.createElement('canvas');
  cv.width = Math.max(2, Math.ceil((adv + Math.abs(st.slant) + 2 * mx) * s));
  cv.height = Math.max(2, Math.ceil((1 + DESC + 2 * my) * s));
  paint(cv.getContext('2d'), items, st, s, mx * s, my * s);

  const out = {
    url: cv.toDataURL('image/png'),
    w: cv.width,
    h: cv.height,
    capPx: s,
    baseline: my * s + s,
    ratio: cv.height / s,
    aspect: cv.width / cv.height,
  };
  if (imgCache.size > CACHE_MAX) {
    // Cheap eviction: drop the oldest quarter. Strings here are short-lived
    // labels, so exact LRU is not worth the bookkeeping.
    let n = Math.floor(CACHE_MAX / 4);
    for (const k of imgCache.keys()) { imgCache.delete(k); if (--n <= 0) break; }
  }
  imgCache.set(key, out);
  return out;
}

/**
 * A vertical strip of the ten figures, for odometer digit columns. Cell height
 * is uniform and every figure shares one advance, so translating the strip by
 * exactly one cell advances one digit and nothing shifts sideways.
 */
const stripCache = new Map();
export function digitStrip(st = STYLES.hud) {
  const key = keyOf(st);
  const hit = stripCache.get(key);
  if (hit) return hit;

  const s = st.capPx;
  const { mx, my } = padOf(st);
  const cellW = Math.ceil((GLYPHS[0].w + Math.abs(st.slant) + 2 * mx) * s);
  const cellH = Math.ceil((1 + DESC + 2 * my) * s);

  const cv = document.createElement('canvas');
  cv.width = cellW;
  cv.height = cellH * 10;
  const g = cv.getContext('2d');
  for (let d = 0; d < 10; d++) {
    const { items } = layout(String(d), st);
    paint(g, items, st, s, mx * s, my * s + d * cellH);
  }

  const out = {
    url: cv.toDataURL('image/png'),
    cellW, cellH,
    capPx: s,
    ratio: cellH / s,
    aspect: cellW / cellH,
  };
  stripCache.set(key, out);
  return out;
}

/** Direct canvas drawing, for components that own a canvas (the timing bar). */
export function drawText(g, text, x, y, capPx, st = STYLES.chip) {
  const src = st.uppercase ? text.toUpperCase() : text;
  const s = capPx;
  g.save();
  g.transform(s, 0, -s * st.slant, s, x + s * st.slant, y - s);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  let adv = 0;
  const items = [];
  for (const c of src) {
    const r = resolve(c);
    if (r) items.push({ ch: r, x: adv });
    adv += (r ? GLYPHS[r].w : 0.34) + st.tracking;
  }
  const strokeAll = () => {
    for (const it of items) {
      const p = pathFor(it.ch);
      if (!p) continue;
      g.save(); g.translate(it.x, 0); g.stroke(p); g.restore();
    }
  };
  g.strokeStyle = st.outline;
  g.lineWidth = st.pen + 2 * OUTLINE;
  strokeAll();
  g.strokeStyle = hex(st.color);
  g.lineWidth = st.pen;
  strokeAll();
  g.restore();
  return adv * capPx;
}

export const Font = {
  GLYPHS, STYLES, style, textImage, digitStrip, advanceOf, drawText, PEN, OUTLINE,
};
