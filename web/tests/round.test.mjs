/**
 * Count-in tests. The count-in is the one thing every minigame shows before
 * the player has done anything, so an off-by-one here is visible in all five.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { countIn } from '../src/core/round.js';

/** Minimal ctx: records what was shown and ticked, replays a beat range. */
function fakeCtx() {
  let listener = null;
  const shown = [];
  const ticks = [];
  return {
    shown,
    ticks,
    onBeat(fn) { listener = fn; return () => { listener = null; }; },
    audio: { sfx: (name, t, i) => ticks.push([name, i]) },
    ui: { banner: (text) => shown.push(text) },
    run(from, to) { for (let b = from; b <= to; b++) listener?.(b, b * 0.5); },
  };
}

test('default count-in speaks 4 3 2 1 across the last four lead-in beats', () => {
  const ctx = fakeCtx();
  countIn(ctx, { beats: 8 });
  ctx.run(-8, 2);
  assert.deepEqual(ctx.shown, ['4', '3', '2', '1']);
  assert.equal(ctx.ticks.length, 8, 'the tick plays on every lead-in beat');
});

test('a `go` shifts the numbers down one so the last beat says it instead', () => {
  // Swing Kings: "3 2 1 PLAY BALL!", not "4 3 2 1 PLAY BALL!" — the lead-in
  // is tuned to be gone before the first scored pitch reaches the plate.
  const ctx = fakeCtx();
  countIn(ctx, { beats: 8, from: 3, go: 'PLAY BALL!', show: (t) => ctx.shown.push(t) });
  ctx.run(-8, 2);
  assert.deepEqual(ctx.shown, ['3', '2', '1', 'PLAY BALL!']);
});

test('goOnDownbeat keeps the numbers and puts `go` on beat 0', () => {
  // Bounce Brigade: "3 2 1" then "GO!" on the downbeat.
  const ctx = fakeCtx();
  countIn(ctx, { beats: 8, from: 3, go: 'GO!', goOnDownbeat: true, show: (t) => ctx.shown.push(t) });
  ctx.run(-8, 2);
  assert.deepEqual(ctx.shown, ['3', '2', '1', 'GO!']);
});

test('nothing is spoken or ticked once the round has started', () => {
  const ctx = fakeCtx();
  countIn(ctx, { beats: 8 });
  ctx.run(0, 16);
  assert.deepEqual(ctx.shown, []);
  assert.deepEqual(ctx.ticks, []);
});

test('the tick index cycles 0..3 without JS modulo sign bugs', () => {
  // `b % 4` on a negative beat yields a negative index; the lead-in runs
  // entirely on negative beats.
  const ctx = fakeCtx();
  countIn(ctx, { beats: 8 });
  ctx.run(-8, -1);
  assert.deepEqual(ctx.ticks.map((t) => t[1]), [0, 1, 2, 3, 0, 1, 2, 3]);
});

test('extra per-beat work runs on every beat, inside the lead-in and after', () => {
  const ctx = fakeCtx();
  const seen = [];
  countIn(ctx, { beats: 8, onBeat: (b) => seen.push(b) });
  ctx.run(-10, 3);
  assert.deepEqual(seen, [-10, -9, -8, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3]);
});

test('the unsubscribe is returned', () => {
  const ctx = fakeCtx();
  const off = countIn(ctx, { beats: 8 });
  assert.equal(typeof off, 'function');
  off();
  ctx.run(-4, -1);
  assert.deepEqual(ctx.shown, [], 'nothing fires after unsubscribe');
});
