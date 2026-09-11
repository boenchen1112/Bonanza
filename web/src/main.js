/**
 * Boot + frame loop + scene routing.  [INTEGRATOR-owned — do not edit in agent work]
 *
 * Frame order is deliberate and load-bearing:
 *   1. clock.tick()        — sync clock domains, dispatch lookahead audio
 *   2. input.drain()       — presses, with their true audio timestamps
 *   3. scene.input()       — gameplay reacts on the SAME frame as the press
 *   4. scene.update()      — simulation
 *   5. stage.render()      — draw
 *
 * Reacting to input before updating (rather than after) removes exactly one
 * frame of response latency, which is the difference between "responsive" and
 * "fine, I guess".
 */

import * as THREE from 'three';
import { Clock } from './core/clock.js';
import { Input } from './core/input.js';
import { FEEL } from './core/feel.js';
import { Bus, makeRng, Save } from './core/util.js';
import { createStage } from './render/stage.js';
import { createAudio } from './audio/index.js';
import { createUI } from './ui/index.js';
import { createFX } from './render/fx/index.js';
import { SCENES, getScene } from './shell/registry.js';
import { resolveActivation } from './shell/nav.js';

const canvas = document.getElementById('stage');
const uiRoot = document.getElementById('ui');

const audioCtx = new (window.AudioContext || window.webkitAudioContext)({
  latencyHint: 'interactive',
});

const clock = new Clock(audioCtx);
clock.refreshOutputLatency();

const input = new Input(clock);
const bus = new Bus();

const stage = createStage({ canvas, clock });
const audio = createAudio({ ctx: audioCtx, clock, bus });
const ui = createUI({ root: uiRoot, bus, clock });
const fx = createFX({ stage, clock, bus });

/**
 * `window.__BBB__` (the harness/test API: autoplay, telemetry, goto, the
 * audio tap) exists in dev and in `vite build --mode harness` builds only.
 * Vite inlines these as literals, so a production bundle drops the whole
 * block rather than shipping a remote control for the game.
 */
const TEST_API = Boolean(import.meta.env.DEV || import.meta.env.MODE === 'harness');

/** @type {import('./shell/registry.js').SceneModule|null} */
let current = null;
let currentCtx = null;
/** Last scene that threw in load()/start(), for the test API. */
let lastError = null;
let pendingScene = null;
let hitstopUntil = 0;
/** When the current hitstop began, so we only ever subtract time once. */
let frozenFrom = 0;

/**
 * Harness autoplay.
 *
 * Driven from inside the frame loop rather than from setTimeout, because under
 * software rendering a frame can take 350ms and a timer-driven bot then
 * delivers every press hundreds of ms late — turning a perfectly good game into
 * a wall of false misses. Here the bot emits each press with the audio time it
 * WAS AIMED AT, so judgement measures the judge, not the frame rate.
 */
let bot = null;
let botPrevBeat = null;

let botPressTotal = 0;

function botPress(action, t) {
  const ev = { action, time: t, down: true, source: 'bot', repeat: false };
  if (current) {
    try { current.input?.(currentCtx, [ev]); } catch (e) { console.error(e); }
  }
  bot.presses++;
  botPressTotal++;
}

function pumpBot(beat) {
  if (!bot) return;
  if (clock.now() > bot.until) { bot = null; botPrevBeat = null; return; }

  // Chart mode: press only the scene's actual notes (see `testChart`), the
  // way a person plays — one press per note instead of one per grid step.
  if (bot.chart) {
    const now = clock.now();
    while (bot.cursor < bot.chart.length && bot.chart[bot.cursor].time <= now) {
      const n = bot.chart[bot.cursor++];
      if (bot.rng() < bot.missRate) continue;
      botPress(n.action || 'a', n.time + (bot.rng() * 2 - 1) * bot.jitter);
    }
    return;
  }

  if (botPrevBeat === null) { botPrevBeat = beat; return; }

  // Every subdivision boundary crossed since the last frame gets a press.
  const div = bot.division;
  let k = Math.floor(botPrevBeat * div) + 1;
  const kEnd = Math.floor(beat * div);
  let guard = 0;
  while (k <= kEnd && guard++ < 64) {
    const targetBeat = k / div;
    k++;
    if (bot.rng() < bot.missRate) continue;
    const off = (bot.rng() * 2 - 1) * bot.jitter;
    const t = clock.timeAt(targetBeat) + off;
    // Press every configured action, not just one. A lane game has no single
    // button to press, and a bot that only knows 'a' scores zero on it by
    // construction — which reads as a broken game and is not. Extra presses
    // are harmless: the judge swallows a press that no note claims rather
    // than burning one, so covering all lanes measures the chart honestly.
    for (const action of bot.actions) botPress(action, t);
  }
  botPrevBeat = beat;
}

const ctxBase = {
  clock, input, bus, audio, ui, fx, stage,
  renderer: stage.renderer,
  rng: makeRng(0x5eed),
  players: [],
  size: stage.size,
  THREE,
  /** Freeze gameplay time briefly to sell an impact. */
  hitstop(seconds) {
    const now = performance.now() / 1000;
    if (now >= hitstopUntil) frozenFrom = now;
    hitstopUntil = Math.max(hitstopUntil, now + seconds);
  },
  /**
   * Ask the shell to move on. Routed through `resolveActivation` so a scene
   * that calls `go(gameId)` directly (as `select.js` used to) gets silently
   * corrected into the `play` wrapper rather than skipping pause/results.
   */
  go(sceneId, opts) {
    try {
      pendingScene = resolveActivation(sceneId, opts);
    } catch (e) {
      console.error('go: routing rejected', sceneId, e);
    }
  },
};

// --------------------------------------------------------------- scene swap

async function activate(id, opts = {}) {
  if (current) {
    try { current.dispose?.(currentCtx); } catch (e) { console.error('dispose', e); }
    // No scene until the next one has loaded: the loop kept calling the
    // disposed one's update/input through the await, and when the next scene
    // is the same module (play → play on restart) that reached its half-built state.
    current = null;
    stage.detach();
    ui.clear();
    fx.reset();
    clock.stop();
    clock.clearSchedule();
  }

  const mod = await getScene(id);
  if (!mod) { console.error('unknown scene', id); return; }

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(50, stage.size.w / stage.size.h, 0.1, 200);
  camera.position.set(0, 2.2, 8);

  currentCtx = Object.assign(Object.create(ctxBase), {
    scene, camera, opts,
    rng: makeRng(opts.seed ?? 0x5eed),
  });

  stage.attach(scene, camera);

  // A scene that throws in load() must not take the application down with it.
  // Before this, one game reading a null field during load left `ready`
  // permanently unresolved: the whole app hung at a black screen, and the
  // harness reported a timeout rather than the actual error. A broken scene
  // should be a broken scene, not a broken product.
  try {
    await mod.load?.(currentCtx);
    stage.sceneId = id;
    await stage.warm();
    current = mod;
    mod.start?.(currentCtx);
    bus.emit('scene:active', id);
  } catch (e) {
    console.error('scene failed to start:', id, e);
    current = null;
    stage.sceneId = null;
    lastError = { scene: id, message: String(e && e.message || e) };
    bus.emit('scene:error', id, e);
    // Fall back to the title screen so the player is never stranded — unless
    // the title is what failed, in which case stop rather than loop forever.
    if (id !== 'title') pendingScene = { id: 'title', opts: {} };
  }
}

// ---------------------------------------------------------------- main loop

let last = performance.now();

function frame(nowMs) {
  requestAnimationFrame(frame);

  const nowS = nowMs / 1000;
  const rawDt = (nowMs - last) / 1000;
  let dt = Math.min(rawDt, FEEL.maxDt);
  last = nowMs;
  if (rawDt > FEEL.maxDt * 1.5) perf.onStall(rawDt);

  clock.tick(nowMs);
  input.pollGamepads(nowMs);

  // Hitstop freezes gameplay, not the transport. The music never stutters.
  //
  // Subtract only the frozen PORTION of this frame rather than zeroing dt
  // outright. Zeroing means a 78ms hitstop consumes a whole frame — fine at
  // 60fps (78ms is ~5 frames), catastrophic on a device dropping to 15fps,
  // where one hitstop swallows 66ms of animation it was never meant to touch
  // and callouts visibly pile up on screen.
  if (nowS < hitstopUntil) {
    const frameStart = nowS - dt;
    const frozen = Math.min(hitstopUntil, nowS) - Math.max(frameStart, frozenFrom);
    dt = Math.max(0, dt - Math.max(0, frozen));
  }

  pumpBot(clock.beat);

  const events = input.drain();
  if (current) {
    if (events.length) {
      try { current.input?.(currentCtx, events); } catch (e) { console.error(e); }
    }
    try { current.update?.(currentCtx, dt, clock.beat); } catch (e) { console.error(e); }
  }

  fx.update(dt);
  ui.update(dt);

  // CPU cost of OUR code, measured separately from the draw. Under software
  // rendering (CI, the critic harness) total frame time says nothing about the
  // game; this number still does.
  const cpuEnd = performance.now();
  stage.render(dt, nowS);

  telemetry.push(nowMs, cpuEnd - nowMs);
  perf.update(nowMs);

  if (pendingScene) {
    const p = pendingScene;
    pendingScene = null;
    activate(p.id, p.opts || {});
  }
}

// -------------------------------------------------------------- telemetry

const telemetry = {
  frames: [],
  cpu: [],
  judgements: [],
  _lastMs: performance.now(),
  push(nowMs, cpuMs) {
    const d = nowMs - this._lastMs;
    this._lastMs = nowMs;
    this.frames.push(d);
    this.cpu.push(cpuMs);
    if (this.frames.length > 1800) { this.frames.shift(); this.cpu.shift(); }
  },
  _stats(arr) {
    const f = arr.slice().sort((a, b) => a - b);
    const pct = (p) => (f.length ? f[Math.min(f.length - 1, Math.floor(f.length * p))] : 0);
    const mean = f.length ? f.reduce((a, b) => a + b, 0) / f.length : 0;
    return { mean, p50: pct(0.5), p95: pct(0.95), p99: pct(0.99), max: f[f.length - 1] || 0 };
  },
  snapshot() {
    const frameMs = this._stats(this.frames);
    const info = stage.renderer.info;
    return {
      frameCount: this.frames.length,
      fps: frameMs.mean ? 1000 / frameMs.mean : 0,
      frameMs,
      /** Our JS per frame. THIS is the perf number that survives a software GPU. */
      cpuMs: this._stats(this.cpu),
      render: {
        // From the stage, captured before post's fullscreen quads overwrite
        // renderer.info — otherwise every scene reports "1 draw call".
        drawCalls: stage.stats?.drawCalls ?? info.render.calls,
        triangles: stage.stats?.triangles ?? info.render.triangles,
        postPasses: stage.stats?.postPasses ?? 0,
        programs: info.programs?.length ?? 0,
        geometries: info.memory.geometries,
        textures: info.memory.textures,
      },
      judgements: this.judgements.slice(-400),
      scene: stage.sceneId,
      outputLatencyMs: clock.outputLatency * 1000,
    };
  },
};
bus.on('judge', (j) => telemetry.judgements.push({
  verdict: j.verdict, errMs: j.errMs, beat: j.beat ?? null, t: clock.now(),
}));

// ------------------------------------------------------------------ perf HUD
//
// A real-hardware profiling overlay. The critic harness's SwiftShader numbers
// are known to be meaningless (see docs/HANDOFF.md §3) — this exists so a
// player hitting real lag/audio-dropout/scoring-drift on real hardware can
// turn it on, reproduce the problem, and report back what it actually reads.
// Toggle with the ` (backquote) key, or start visible with ?perf=1.

const perf = (() => {
  let el = null;
  let visible = false;
  let lastPaint = 0;
  let stallCount = 0;
  let worstStallMs = 0;

  function ensure() {
    if (el) return el;
    el = document.createElement('div');
    el.style.cssText = 'position:fixed;left:8px;bottom:8px;z-index:9999;'
      + 'font:11px/1.5 ui-monospace,Consolas,monospace;color:#c9ffb0;'
      + 'background:rgba(4,6,10,.78);border:1px solid rgba(180,255,150,.35);'
      + 'border-radius:6px;padding:6px 9px;white-space:pre;pointer-events:none;'
      + 'text-shadow:0 1px 0 rgba(0,0,0,.8);';
    document.body.appendChild(el);
    return el;
  }

  return {
    onStall(rawDtSeconds) {
      stallCount++;
      worstStallMs = Math.max(worstStallMs, rawDtSeconds * 1000);
    },
    toggle(force) {
      visible = force !== undefined ? force : !visible;
      if (visible) { ensure().style.display = 'block'; } else if (el) { el.style.display = 'none'; }
    },
    update(nowMs) {
      if (!visible || nowMs - lastPaint < 250) return;
      lastPaint = nowMs;
      const s = telemetry.snapshot();
      ensure().textContent =
        `fps ${s.fps.toFixed(0)}  cpu ${s.cpuMs.mean.toFixed(1)}ms (p95 ${s.cpuMs.p95.toFixed(1)})\n`
        + `draws ${s.render.drawCalls}  tris ${s.render.triangles}\n`
        + `audio ${audioCtx.state}  latency ${s.outputLatencyMs.toFixed(0)}ms\n`
        + `stalls ${stallCount} (worst ${worstStallMs.toFixed(0)}ms)  scene ${s.scene || '-'}`;
    },
  };
})();
window.addEventListener('keydown', (e) => { if (e.code === 'Backquote') perf.toggle(); }, { passive: true });
if (new URLSearchParams(location.search).has('perf')) perf.toggle(true);

// ------------------------------------------------------------- resize / focus

function resize() {
  stage.resize(window.innerWidth, window.innerHeight);
  if (currentCtx?.camera) {
    currentCtx.camera.aspect = stage.size.w / stage.size.h;
    currentCtx.camera.updateProjectionMatrix();
  }
  bus.emit('resize', stage.size);
}
window.addEventListener('resize', resize, { passive: true });
window.addEventListener('orientationchange', resize, { passive: true });

// Browser/OS zoom (Ctrl+/-, pinch-zoom) changes devicePixelRatio and the
// effective viewport without reliably firing 'resize' in every browser. The
// DOM UI layer re-derives its sizing from vmin on every paint regardless, so
// it always tracks zoom — but the WebGL canvas only resamples its buffer size
// and DPR inside resize() above, so without this it drifts out of sync with
// the DOM and renders stretched or misaligned. visualViewport catches both
// cases in one shared path, rAF-coalesced so a zoom gesture doesn't spam it.
if (window.visualViewport) {
  let queued = false;
  const onViewport = () => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; resize(); });
  };
  window.visualViewport.addEventListener('resize', onViewport, { passive: true });
}

// Autoplay policy: the context starts suspended until a real gesture.
async function unlock() {
  if (audioCtx.state !== 'running') {
    try { await audioCtx.resume(); } catch { /* ignore */ }
  }
  clock.refreshOutputLatency();
  bus.emit('audio:unlocked');
}
for (const ev of ['pointerdown', 'keydown', 'touchstart']) {
  window.addEventListener(ev, unlock, { passive: true });
}

// ------------------------------------------------------------------ test API

let resolveReady = () => {};
if (TEST_API) window.__BBB__ = {
  ready: new Promise((r) => { resolveReady = r; }),
  get scene() { return stage.sceneId; },
  get lastError() { return lastError; },
  scenes: SCENES.map((s) => s.id),
  version: '0.1.0',
  clock, input, bus,
  /** The mix graph (`ctx`, `master`, buses) — the harness taps `master`. */
  audio,
  /** Renderer, look, lights — for debugging render cost from the harness. */
  stage,
  telemetry: () => telemetry.snapshot(),
  resetTelemetry: () => { telemetry.frames.length = 0; telemetry.judgements.length = 0; },
  goto: (id, opts) => activate(id, opts || {}),
  /** Shell bookkeeping (session/profile) — lets a script stage a party mid-way. */
  shellState: () => import('./shell/state.js'),
  setSeed: (n) => { ctxBase.rng = makeRng(n); },
  /**
   * Render quality. The harness forces 'low' by default: it renders through
   * SwiftShader, where the post chain costs hundreds of ms per frame and the
   * resulting frame starvation delays synthetic presses so badly that every
   * note reads as a miss. That is a measurement artifact, not a game defect —
   * dropping post restores a real frame rate so timing can actually be judged.
   */
  setQuality: (tier) => stage.setQuality?.(tier),
  /**
   * Play the game automatically for `seconds`. Frame-rate independent by
   * construction — see pumpBot.
   * @param {{mode?:'perfect'|'auto'|'sloppy', seconds?:number,
   *          division?:number, action?:string, seed?:number}} o
   */
  autoplay(o = {}) {
    const mode = o.mode || 'auto';
    const preset = {
      perfect: { jitter: 0, missRate: 0 },
      auto: { jitter: 0.028, missRate: 0.06 },
      sloppy: { jitter: 0.075, missRate: 0.22 },
    }[mode] || { jitter: 0.028, missRate: 0.06 };
    bot = {
      ...preset,
      actions: o.actions
        ? (Array.isArray(o.actions) ? o.actions : String(o.actions).split(','))
        : [o.action || 'a'],
      division: o.division ?? 2,
      until: clock.now() + (o.seconds ?? 15),
      rng: makeRng(o.seed ?? 0xb07),
      presses: 0,
      // `chart: true` uses the scene's own note list when it offers one.
      chart: null,
      cursor: 0,
    };
    // (a host scene like the shell's `play` forwards testChart and returns
    // null when the game it hosts has none: fall back to division presses)
    const list = o.chart && typeof current?.testChart === 'function' ? current.testChart(currentCtx) : null;
    if (Array.isArray(list)) {
      const now = clock.now();
      bot.chart = list.filter((n) => n.time > now).sort((a, b) => a.time - b.time);
    }
    botPrevBeat = null;
    botPressTotal = 0;
    return true;
  },
  stopAutoplay() { bot = null; botPrevBeat = null; },
  botPresses: () => botPressTotal,
  /** Synthetic press at an exact audio time (or beat) — how the harness plays. */
  press(action = 'a', opts = {}) {
    const t = opts.atBeat !== undefined ? clock.timeAt(opts.atBeat)
      : opts.atTime !== undefined ? opts.atTime
        : clock.now();
    const ev = { action, time: t, down: true, source: 'synthetic', repeat: false };
    if (current) current.input?.(currentCtx, [ev]);
    return ev;
  },
  /** Resolves when the renderer has drawn a settled frame. */
  screenshotReady: () => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))),
  save: Save,
};

// --------------------------------------------------------------------- boot

(async function boot() {
  resize();
  const q = new URLSearchParams(location.search).get('quality');
  if (q) stage.setQuality?.(q);
  await audio.init();
  const startScene = new URLSearchParams(location.search).get('scene') || 'title';
  try {
    await activate(startScene, Object.fromEntries(new URLSearchParams(location.search)));
  } catch (e) {
    console.error('boot scene failed:', e);
  }
  requestAnimationFrame((t) => { last = t; frame(t); });
  resolveReady(true);
})();
