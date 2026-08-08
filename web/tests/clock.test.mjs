/**
 * Clock tests. The transport is the one module where a bug is both invisible
 * and fatal — the game runs, it just drifts.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { Clock, Beats } from '../src/core/clock.js';

/** Minimal AudioContext stand-in; we drive currentTime by hand. */
function fakeCtx(opts = {}) {
  return { currentTime: 0, outputLatency: opts.outputLatency ?? 0, baseLatency: opts.baseLatency ?? 0 };
}

test('beat <-> time round-trips exactly', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(137.13);
  c.start(2.5, 0);
  for (const b of [0, 1, 3.25, 17, 128.5, -8]) {
    assert.ok(Math.abs(c.beatAt(c.timeAt(b)) - b) < 1e-9, `round trip failed at beat ${b}`);
  }
});

test('tempo change preserves beat phase (no jump at the seam)', () => {
  // Finale Fever ramps tempo mid-song. If setBpm restarted the beat count the
  // music would jump; the player would hear a stumble on every ramp step.
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  ctx.currentTime = 4.0; // 8 beats in at 120bpm
  const before = c.beat;
  assert.ok(Math.abs(before - 8) < 1e-9);
  c.setBpm(180);
  assert.ok(Math.abs(c.beat - before) < 1e-9, 'beat number must be continuous across a tempo change');
  ctx.currentTime = 4.0 + 1 / 3; // one beat at 180bpm
  assert.ok(Math.abs(c.beat - (before + 1)) < 1e-9, 'new tempo must apply after the seam');
});

test('a continuous tempo ramp accumulates no drift', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(150);
  c.start(0, 0);
  let expected = 0;
  let bpm = 150;
  const dt = 0.016;
  for (let i = 0; i < 3000; i++) {
    expected += (dt * bpm) / 60;
    ctx.currentTime += dt;
    bpm *= 1.0001;
    c.setBpm(bpm);
  }
  // Each setBpm rebases from the current time, so error is bounded by the
  // per-step quantisation only — it must not compound.
  assert.ok(Math.abs(c.beat - expected) < 0.5, `drift too large: ${c.beat} vs ${expected}`);
});

test('output latency shifts now() so judgement matches what is HEARD', () => {
  const ctx = fakeCtx({ outputLatency: 0.02 });
  const c = new Clock(ctx);
  c.refreshOutputLatency();
  ctx.currentTime = 1.0;
  assert.equal(c.rawNow(), 1.0);
  assert.ok(Math.abs(c.now() - 0.98) < 1e-9, 'now() must be behind rawNow() by the output latency');
});

test('absurd or missing outputLatency falls back rather than poisoning timing', () => {
  for (const bad of [undefined, NaN, -1, 5, null]) {
    const ctx = { currentTime: 0, outputLatency: bad, baseLatency: 0.005 };
    const c = new Clock(ctx);
    assert.equal(c.refreshOutputLatency(), 0.005, `bad value ${bad} must fall back to baseLatency`);
  }
  const noneAtAll = new Clock({ currentTime: 0 });
  assert.equal(noneAtAll.refreshOutputLatency(), 0);
});

test('integer beats fire exactly once, in order, none skipped', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  const fired = [];
  c.onBeat((b) => fired.push(b));
  for (let i = 0; i < 200; i++) { ctx.currentTime += 0.05; c.tick(); }
  assert.deepEqual(fired, fired.slice().sort((a, b) => a - b), 'out of order');
  assert.equal(new Set(fired).size, fired.length, 'a beat fired twice');
  for (let i = 1; i < fired.length; i++) {
    assert.equal(fired[i], fired[i - 1] + 1, `skipped beat between ${fired[i - 1]} and ${fired[i]}`);
  }
  assert.ok(fired.length >= 19, 'too few beats fired');
});

test('one enormous frame (tab switch) fires every intervening beat', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  const fired = [];
  c.onBeat((b) => fired.push(b));
  ctx.currentTime = 10; // 20 beats at once
  c.tick();
  assert.ok(fired.length >= 20, `expected >=20 beats, got ${fired.length}`);
  assert.equal(new Set(fired).size, fired.length);
});

test('scheduled one-shots fire before their time, with the correct time', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  const got = [];
  for (const b of [1, 2, 4, 8]) c.at(b, (t, beat) => got.push({ t, beat, firedAt: ctx.currentTime }));
  for (let i = 0; i < 120; i++) { ctx.currentTime += 0.05; c.tick(); }
  assert.equal(got.length, 4);
  for (const g of got) {
    assert.ok(Math.abs(g.t - c.timeAt(g.beat)) < 1e-9, 'wrong scheduled time passed to callback');
    assert.ok(g.firedAt <= g.t, 'fired late — WebAudio needs the lookahead');
  }
});

test('scheduled events fire in beat order even when added out of order', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  const order = [];
  for (const b of [8, 1, 4, 2]) c.at(b, () => order.push(b));
  for (let i = 0; i < 120; i++) { ctx.currentTime += 0.05; c.tick(); }
  assert.deepEqual(order, [1, 2, 4, 8]);
});

test('stop() halts dispatch and clears the schedule', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  c.setBpm(120);
  c.start(0, 0);
  let n = 0;
  c.onBeat(() => n++);
  c.at(2, () => n += 100);
  c.stop();
  ctx.currentTime = 10;
  c.tick();
  assert.equal(n, 0, 'a stopped transport must dispatch nothing');
});

test('perf->audio conversion converges on the true offset', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  // audio time runs 3.0s ahead of the performance clock
  const OFFSET = 3.0;
  for (let i = 0; i < 2000; i++) {
    const perfMs = i * 16;
    ctx.currentTime = perfMs / 1000 + OFFSET;
    c.tick(perfMs);
  }
  const perfMs = 2000 * 16;
  ctx.currentTime = perfMs / 1000 + OFFSET;
  assert.ok(Math.abs(c.toAudioTime(perfMs) - ctx.currentTime) < 0.002,
    'converted event time must land within 2ms of true audio time');
  assert.equal(c.domainReady, true);
});

test('toAudioTime before any sync returns now() rather than nonsense', () => {
  const ctx = fakeCtx();
  const c = new Clock(ctx);
  ctx.currentTime = 5;
  assert.equal(c.toAudioTime(123456), 5);
});

test('Beats helpers', () => {
  assert.ok(Math.abs(Beats.phaseError(4.1) - 0.1) < 1e-9);
  assert.ok(Math.abs(Beats.phaseError(3.9) + 0.1) < 1e-9);
  assert.ok(Math.abs(Beats.frac(7.25) - 0.25) < 1e-9);
  assert.ok(Math.abs(Beats.barFrac(6, 4) - 0.5) < 1e-9);
  assert.ok(Beats.barFrac(-1, 4) >= 0, 'negative beats (lead-in) must not go negative');
});
