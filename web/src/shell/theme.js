/**
 * Shell look-and-feel.  [shell agent owns this file]
 *
 * One stylesheet, one palette, one set of DOM primitives for every screen
 * outside the minigames. The shell is the thing that makes five minigames read
 * as one product, so the panels, the type scale, the shadow lip and the
 * selection feel are defined exactly once, here.
 *
 * Everything is procedural: no image files, no webfonts, no fetches.
 */

import { clamp01, beatPhase } from '../core/util.js';
import { profile } from './state.js';

export const PAL = {
  bg: '#0b0a1a',
  ink: '#080714',
  panel: '#1b1940',
  panel2: '#141230',
  text: '#ffffff',
  dim: '#a9b0e0',
  yellow: '#ffd93d',
  cyan: '#4dd6ff',
  coral: '#ff5d73',
  green: '#9ee87a',
  violet: '#c08cff',
  orange: '#ff9f45',
  teal: '#39d4b4',
  pink: '#ff7ad9',
};

export const RANK_COLOR = { S: PAL.yellow, A: PAL.cyan, B: PAL.green, C: PAL.orange, D: PAL.coral };

export const hex = (n) => (typeof n === 'number' ? '#' + (n >>> 0).toString(16).padStart(6, '0') : n);
export const num = (s) => (typeof s === 'string' ? parseInt(s.replace('#', ''), 16) : s);

/** Thousands separators without Intl allocation churn. */
export function fmtScore(n) {
  n = Math.max(0, Math.round(n || 0));
  const s = String(n);
  let out = '';
  for (let i = 0; i < s.length; i++) {
    if (i > 0 && (s.length - i) % 3 === 0) out += ',';
    out += s[i];
  }
  return out;
}

/** `ctx.audio.sfx` may be mid-rewrite by another agent; never let it throw. */
export function sfx(ctx, name, at) {
  try { ctx.audio?.sfx?.(name, at); } catch { /* audio must never break a menu */ }
}

/** Optional kick on the beat, if the audio agent still exposes voices. */
export function kick(ctx, t, gain = 0.45) {
  try { ctx.audio?.voices?.kick?.(t, { gain }); } catch { /* ignore */ }
}

/** The UI agent owns `ui.layer`; fall back to the raw overlay if it moves. */
export function uiLayer(ctx) {
  return ctx.ui?.layer || document.getElementById('ui') || document.body;
}

/**
 * Mount a screen root inside the UI layer. `ui.clear()` (called by the router
 * on every scene swap) removes it for us; dispose() removes it too, so a scene
 * that is torn down early never leaves DOM behind.
 */
export function mountRoot(ctx, cls = '') {
  injectShellStyles();
  const d = document.createElement('div');
  d.className = 'sh-root ' + cls;
  uiLayer(ctx).appendChild(d);
  return d;
}

export function el(tag, cls = '', text = '') {
  const d = document.createElement(tag);
  if (cls) d.className = cls;
  if (text !== '' && text !== null && text !== undefined) d.textContent = String(text);
  return d;
}

/** A chunky panel with the series' signature shadow lip. */
export function panel(cls = '', accent = PAL.cyan) {
  const d = el('div', 'sh-panel ' + cls);
  d.style.setProperty('--accent', accent);
  return d;
}

/** Big display text with the outline + drop treatment used series-wide. */
export function display(text, cls = '') {
  return el('div', 'sh-display ' + cls, text);
}

/** 0..1 sawtooth of the current beat, for pulses that must land ON the beat. */
export function beatPulse(beat, sharpness = 6) {
  const f = beatPhase(beat);
  return Math.exp(-f * sharpness);
}

/** Softer, symmetric breathing — for idle poses that must never look frozen. */
export function breathe(t, hz = 0.5) {
  return 0.5 + 0.5 * Math.sin(t * hz * Math.PI * 2);
}

/** Cheap deterministic per-index jitter so rows never move in lockstep. */
export const stagger = (i, amount = 0.12) => ((i * 0.6180339887) % 1) * amount;

export function reducedMotion() {
  let osWants = false;
  try { osWants = window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { /* no matchMedia */ }
  return osWants || !!profile?.options?.reduceMotion;
}

/** Progress helper: seconds since a mark, eased 0..1 over `dur`. */
export const since = (t, mark, dur) => clamp01((t - mark) / dur);

/**
 * Screen wipe. Every shell screen reveals with one and covers with one, so a
 * scene swap never shows the single-frame gap where the old scene is gone and
 * the new one has not painted. `play()` runs the cover and calls back when the
 * screen is fully hidden — that is the moment to `ctx.go`.
 */
export function createWipe(root, { color = PAL.yellow, mode = 'in' } = {}) {
  const d = el('div', 'sh-wipe');
  d.style.background = `linear-gradient(100deg, ${color} 0%, ${shift(color)} 100%)`;
  root.appendChild(d);
  let t = 0;
  let dir = mode; // 'in' = reveal, 'out' = cover, 'idle'
  let cb = null;
  const DUR = 0.30;
  apply(0);

  function apply(p) {
    const x = dir === 'in' ? -140 * p : 140 * (1 - p);
    d.style.transform = `translateX(${x}%) skewX(-9deg)`;
    d.style.display = (dir === 'in' && p >= 1) ? 'none' : 'block';
  }
  function update(dt) {
    if (dir === 'idle') return;
    t += dt;
    const p = clamp01(t / DUR);
    apply(p * p * (3 - 2 * p));
    if (p >= 1) {
      const done = cb; cb = null;
      if (dir === 'out') { d.style.display = 'block'; d.style.transform = 'translateX(0%) skewX(-9deg)'; }
      dir = 'idle';
      if (done) done();
    }
  }
  function play(fn, col) {
    if (col) d.style.background = `linear-gradient(100deg, ${col} 0%, ${shift(col)} 100%)`;
    dir = 'out'; t = 0; cb = fn; d.style.display = 'block'; apply(0);
  }
  return { el: d, update, play, get busy() { return dir === 'out'; } };
}

function shift(c) {
  if (c === PAL.yellow) return PAL.orange;
  if (c === PAL.cyan) return PAL.violet;
  if (c === PAL.coral) return PAL.pink;
  return PAL.cyan;
}

let injected = false;
export function injectShellStyles() {
  if (injected || document.getElementById('sh-style')) { injected = true; return; }
  injected = true;
  const s = document.createElement('style');
  s.id = 'sh-style';
  s.textContent = CSS;
  document.head.appendChild(s);
}

const CSS = `
.sh-root{position:absolute;inset:0;color:#fff;pointer-events:none;
  font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
  -webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility;
  container-type:size;}
.sh-root *{box-sizing:border-box;}

/* ---------------------------------------------------------------- display */
.sh-display{font-weight:900;letter-spacing:-.03em;line-height:.94;
  text-shadow:0 .06em 0 rgba(0,0,0,.65),0 .12em .3em rgba(0,0,0,.5);}
.sh-sub{font-weight:700;color:${PAL.dim};letter-spacing:.02em;
  text-shadow:0 2px 0 rgba(0,0,0,.6);}
.sh-mono{font-variant-numeric:tabular-nums;}

/* ------------------------------------------------------------------ logo */
.sh-logo{position:absolute;left:50%;top:22%;transform:translate(-50%,-50%);
  text-align:center;white-space:nowrap;will-change:transform;}
.sh-logo__line{display:block;white-space:nowrap;}
.sh-logo__l{display:inline-block;font-weight:900;letter-spacing:-.04em;
  will-change:transform;transform-origin:50% 85%;
  background:linear-gradient(178deg,#fff 6%,${PAL.yellow} 42%,#ff9f45 68%,${PAL.coral} 100%);
  -webkit-background-clip:text;background-clip:text;color:transparent;
  /* No text-shadow here: with a transparent, background-clipped fill Chrome
     paints the shadow OVER the gradient, which turned every letter face dark
     brown. The extrusion lives in the filter chain instead. */
  filter:drop-shadow(0 .055em 0 rgba(20,10,48,.9)) drop-shadow(0 .02em 0 #2a1d5e) drop-shadow(0 0 .34em rgba(255,190,90,.45));}
.sh-logo__l--alt{background:linear-gradient(178deg,#fff 6%,${PAL.cyan} 40%,#7aa6ff 70%,${PAL.violet} 100%);
  -webkit-background-clip:text;background-clip:text;
  filter:drop-shadow(0 .055em 0 rgba(8,14,44,.9)) drop-shadow(0 .02em 0 #16264f) drop-shadow(0 0 .34em rgba(90,200,255,.45));}
.sh-logo__l--sp{width:.34em;}
.sh-logo__tag{margin-top:.34em;font-weight:900;letter-spacing:.42em;
  color:${PAL.dim};text-shadow:0 2px 0 rgba(0,0,0,.7);}

/* ----------------------------------------------------------------- panel */
.sh-panel{position:relative;background:linear-gradient(180deg,${PAL.panel} 0%,${PAL.panel2} 100%);
  border:2px solid rgba(255,255,255,.10);border-radius:18px;
  box-shadow:0 7px 0 rgba(0,0,0,.55),inset 0 2px 0 rgba(255,255,255,.14),
             0 0 0 0 var(--accent);
  --accent:${PAL.cyan};will-change:transform,box-shadow;}
.sh-panel--sel{border-color:var(--accent);
  box-shadow:0 9px 0 rgba(0,0,0,.55),inset 0 2px 0 rgba(255,255,255,.22),
             0 0 26px -2px var(--accent);}

/* ------------------------------------------------------------------ menu */
.sh-menu{position:absolute;left:9%;bottom:9%;display:flex;flex-direction:column;gap:.62em;
  font-size:clamp(15px,2.05vw,27px);}
.sh-item{position:relative;display:flex;align-items:center;gap:.6em;
  padding:.44em 1.5em .5em .8em;border-radius:14px;font-weight:900;letter-spacing:-.01em;
  color:#e9ecff;background:rgba(20,18,48,.55);border:2px solid rgba(255,255,255,.07);
  box-shadow:0 5px 0 rgba(0,0,0,.45);will-change:transform;transform-origin:0% 50%;
  text-shadow:0 2px 0 rgba(0,0,0,.6);}
.sh-item__dot{width:.5em;height:.5em;border-radius:50%;background:var(--accent,${PAL.cyan});
  box-shadow:0 0 12px var(--accent,${PAL.cyan});opacity:.35;transition:opacity .08s;}
.sh-item__sub{font-size:.52em;font-weight:700;color:${PAL.dim};margin-left:.4em;
  letter-spacing:.04em;}
.sh-item--sel{color:#fff;background:linear-gradient(90deg,rgba(255,255,255,.20),rgba(255,255,255,.04));
  border-color:var(--accent,${PAL.cyan});box-shadow:0 6px 0 rgba(0,0,0,.5),0 0 30px -6px var(--accent,${PAL.cyan});}
.sh-item--sel .sh-item__dot{opacity:1;}
.sh-ping{position:absolute;inset:-3px;border-radius:16px;border:3px solid var(--accent,${PAL.cyan});
  pointer-events:none;animation:sh-ping .44s cubic-bezier(.2,.7,.3,1) forwards;}
@keyframes sh-ping{from{transform:scale(1);opacity:.95;}to{transform:scale(1.14,1.6);opacity:0;}}

/* --------------------------------------------------------------- hints */
.sh-hint{position:absolute;right:3.2%;bottom:3.4%;display:flex;gap:1.1em;align-items:center;
  font-size:clamp(11px,1.25vw,16px);font-weight:800;color:${PAL.dim};
  text-shadow:0 2px 0 rgba(0,0,0,.6);}
.sh-key{display:inline-flex;align-items:center;justify-content:center;min-width:2.1em;
  padding:.16em .5em;margin-right:.42em;border-radius:7px;background:rgba(255,255,255,.13);
  border:1px solid rgba(255,255,255,.2);box-shadow:0 2px 0 rgba(0,0,0,.5);color:#fff;font-weight:900;}

/* -------------------------------------------------------------- portraits */
.sh-portrait{display:block;border-radius:14px;image-rendering:auto;}
.sh-card{position:absolute;will-change:transform,opacity;}

/* ---------------------------------------------------------------- stamps */
.sh-stamp{position:absolute;font-weight:900;letter-spacing:-.02em;
  color:#fff;padding:.06em .34em;border-radius:10px;border:.09em solid #fff;
  text-shadow:0 3px 0 rgba(0,0,0,.5);box-shadow:0 4px 0 rgba(0,0,0,.45);
  will-change:transform,opacity;}

/* ------------------------------------------------------------------ bars */
.sh-bar{position:relative;height:100%;border-radius:999px;
  background:linear-gradient(90deg,rgba(255,255,255,.35),var(--accent,${PAL.cyan}));
  box-shadow:inset 0 2px 0 rgba(255,255,255,.35),0 0 18px -4px var(--accent,${PAL.cyan});
  will-change:width;}
.sh-track{position:relative;background:rgba(0,0,0,.42);border-radius:999px;overflow:hidden;
  border:1px solid rgba(255,255,255,.08);}

/* --------------------------------------------------------------- overlay */
.sh-veil{position:absolute;inset:0;background:radial-gradient(120% 90% at 50% 40%,
  rgba(8,7,20,.55) 0%,rgba(8,7,20,.86) 70%,rgba(8,7,20,.95) 100%);will-change:opacity;}
.sh-vign{position:absolute;inset:0;pointer-events:none;
  background:radial-gradient(120% 100% at 50% 45%,rgba(0,0,0,0) 45%,rgba(0,0,0,.55) 100%);}

/* ------------------------------------------------------------------ wipe */
.sh-wipe{position:absolute;left:-25%;top:-12%;width:150%;height:124%;
  will-change:transform;z-index:40;box-shadow:0 0 60px rgba(0,0,0,.6);}
.sh-play__card{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%) rotate(-3deg);
  z-index:41;pointer-events:none;white-space:nowrap;color:#fff;font-size:clamp(34px,6.5vw,92px);
  text-shadow:0 .07em 0 rgba(0,0,0,.35);}

/* --------------------------------------------------------------- scanline */
.sh-attract{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
  will-change:opacity;}
`;
