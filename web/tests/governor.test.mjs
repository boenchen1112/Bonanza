/**
 * Render governor: frame durations and scene phase in, ladder moves out.
 * Pure and deterministic — no clock reads, no randomness — so a frame-time
 * history fully determines what it does.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { createGovernor } from '../src/render/governor.js';
import { ladderFor } from '../src/render/quality.js';

const LADDER = ladderFor(2);      // 7 steps: (2,high) .. (1,low)

/** Feed `seconds` of frames at `ms` each in `phase`; collect decisions. */
function feed(gov, ms, seconds, phase) {
  const out = [];
  const n = Math.round((seconds * 1000) / ms);
  for (let i = 0; i < n; i++) {
    const d = gov.sample(ms, phase);
    if (d) out.push(d);
  }
  return out;
}

/** A played round: `seconds` of play, then a return to the menus. */
function round(gov, ms, seconds = 40) {
  return [...feed(gov, ms, seconds, 'playing'), ...feed(gov, 16.7, 0.5, 'between')];
}

test('sustained severe overload mid-round steps down once, immediately', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  const d = feed(gov, 36, 30, 'playing');
  assert.equal(d.length, 1, 'exactly one step during the round');
  assert.equal(d[0].index, 1);
  assert.deepEqual({ scale: d[0].scale, tier: d[0].tier }, LADDER[1]);
  assert.match(d[0].reason, /overload/);
});

test('mild overload during a round waits for the round to end, then steps down one', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  assert.deepEqual(feed(gov, 20.5, 40, 'playing'), []);
  const d = feed(gov, 16.7, 0.5, 'between');
  assert.equal(d.length, 1);
  assert.equal(d[0].index, 1);
});

test('a mid-round step never crosses into the low tier (a full shader recompile)', () => {
  const lowAt = LADDER.findIndex((l) => l.tier === 'low');
  const gov = createGovernor({ ladder: LADDER, startIndex: lowAt - 1 });
  assert.deepEqual(feed(gov, 40, 20, 'playing'), [], 'no mid-round move into low');
  const d = feed(gov, 16.7, 0.5, 'between');
  assert.equal(d.length, 1, 'the move waits for the round to end');
  assert.equal(d[0].tier, 'low');
});

test('a round start full of load hitches, or a very short round, is not evidence of overload', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  const out = [
    ...feed(gov, 300, 1.8, 'playing'),     // shader compiles as the scene starts
    ...feed(gov, 12, 30, 'playing'),       // then it runs fine
    ...feed(gov, 16.7, 0.5, 'between'),
  ];
  assert.deepEqual(out, [], 'warm-up frames at the start of a round are ignored');
  const short = [...feed(gov, 40, 3, 'playing'), ...feed(gov, 16.7, 0.5, 'between')];
  assert.deepEqual(short, [], 'a 3-second round decides nothing');
});

test('isolated spikes never trigger a step', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  const out = [];
  for (let i = 0; i < 2400; i++) {
    const ms = i % 120 === 0 ? 90 : 16.7;       // one 90ms hitch every 2 seconds
    const d = gov.sample(ms, 'playing');
    if (d) out.push(d);
  }
  out.push(...feed(gov, 16.7, 0.5, 'between'));
  assert.deepEqual(out, []);
});

test('loading and paused frames are ignored', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  assert.deepEqual(feed(gov, 300, 10, 'loading'), []);
  assert.deepEqual(feed(gov, 100, 10, 'paused'), []);
  assert.deepEqual(feed(gov, 16.7, 1, 'between'), []);
});

test('no step up happens mid-round, however much headroom there is', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 3 });
  assert.deepEqual(feed(gov, 8, 60, 'playing'), []);
});

test('steps up one level only after two rounds with clear headroom, at a boundary', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 3 });
  assert.deepEqual(round(gov, 9), [], 'one good round is not enough');
  const d = round(gov, 9);
  assert.equal(d.length, 1);
  assert.equal(d[0].index, 2);
  assert.match(d[0].reason, /headroom/);
});

test('short rounds do not count as evidence of headroom', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 3 });
  for (let i = 0; i < 5; i++) assert.deepEqual(round(gov, 9, 5), []);
});

test('a borderline machine settles instead of oscillating', () => {
  // Level 2 is too slow (21ms), level 3 has headroom (10ms).
  const gov = createGovernor({ ladder: LADDER, startIndex: 2 });
  const perLevel = (i) => (i <= 2 ? 21 : 10);
  let index = 2;
  const moves = [];
  for (let r = 0; r < 12; r++) {
    for (const d of round(gov, perLevel(index))) { moves.push(d.index); index = d.index; }
  }
  assert.deepEqual(moves, [3], 'one step down, never back up into the level that failed');
});

test('never steps below the floor; reports sustained overload at the floor', () => {
  const last = LADDER.length - 1;
  const gov = createGovernor({ ladder: LADDER, startIndex: last });
  let floorReports = 0;
  for (let r = 0; r < 3; r++) {
    for (const d of round(gov, 40)) {
      assert.equal(d.index, last);
      if (d.atFloorOverBudget) floorReports++;
    }
  }
  assert.ok(floorReports >= 1, 'the floor overload is surfaced so the game can hint');
  assert.equal(gov.index, last);
});

test('never steps above the top of the device ladder', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  for (let r = 0; r < 4; r++) assert.deepEqual(round(gov, 8), []);
  assert.equal(gov.index, 0);
});

test('a disabled governor (fixed Graphics setting) makes no decisions', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0, enabled: false });
  assert.deepEqual(feed(gov, 40, 30, 'playing'), []);
  assert.deepEqual(round(gov, 40), []);
});

test('sustained overload in the menus steps down without waiting for a round', () => {
  const gov = createGovernor({ ladder: LADDER, startIndex: 0 });
  const d = feed(gov, 24, 5, 'between');
  assert.equal(d.length, 1);
  assert.equal(d[0].index, 1);
});
