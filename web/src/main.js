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
const fx = createFX({ stage, clock });

/** @type {import('./shell/registry.js').SceneModule|null} */
let current = null;
let currentCtx = null;
let pendingScene = null;
let hitstopUntil = 0;

const ctxBase = {
  clock, input, bus, audio, ui, fx, stage,
  renderer: stage.renderer,
  rng: makeRng(0x5eed),
  players: [],
  size: stage.size,
  THREE,
  /** Freeze gameplay time briefly to sell an impact. */
  hitstop(seconds) {
    hitstopUntil = Math.max(hitstopUntil, performance.now() / 1000 + seconds);
  },
  /** Ask the shell to move on. */
  go(sceneId, opts) { pendingScene = { id: sceneId, opts }; },
};

// --------------------------------------------------------------- scene swap

async function activate(id, opts = {}) {
  if (current) {
    try { current.dispose?.(currentCtx); } catch (e) { console.error('dispose', e); }
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
  await mod.load?.(currentCtx);
  current = mod;
  mod.start?.(currentCtx);
  bus.emit('scene:active', id);
  window.__BBB__.scene = id;
}

// ---------------------------------------------------------------- main loop

let last = performance.now();

function frame(nowMs) {
  requestAnimationFrame(frame);

  const nowS = nowMs / 1000;
  let dt = Math.min((nowMs - last) / 1000, FEEL.maxDt);
  last = nowMs;

  clock.tick(nowMs);
  input.pollGamepads(nowMs);

  // Hitstop freezes gameplay, not the transport. The music never stutters.
  if (nowS < hitstopUntil) dt = 0;

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
        drawCalls: info.render.calls,
        triangles: info.render.triangles,
        programs: info.programs?.length ?? 0,
        geometries: info.memory.geometries,
        textures: info.memory.textures,
      },
      judgements: this.judgements.slice(-400),
      scene: window.__BBB__?.scene,
      outputLatencyMs: clock.outputLatency * 1000,
    };
  },
};
bus.on('judge', (j) => telemetry.judgements.push({
  verdict: j.verdict, errMs: j.errMs, beat: j.beat ?? null, t: clock.now(),
}));

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

let resolveReady;
window.__BBB__ = {
  ready: new Promise((r) => { resolveReady = r; }),
  scene: null,
  scenes: SCENES.map((s) => s.id),
  version: '0.1.0',
  clock, input, bus,
  telemetry: () => telemetry.snapshot(),
  resetTelemetry: () => { telemetry.frames.length = 0; telemetry.judgements.length = 0; },
  goto: (id, opts) => activate(id, opts || {}),
  setSeed: (n) => { ctxBase.rng = makeRng(n); },
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
  await audio.init();
  const startScene = new URLSearchParams(location.search).get('scene') || 'title';
  await activate(startScene, Object.fromEntries(new URLSearchParams(location.search)));
  requestAnimationFrame((t) => { last = t; frame(t); });
  resolveReady(true);
})();
