/**
 * Frame-budget verdict the critic harness reports. Frame durations in, a
 * pass/fail a critic can read without interpreting percentiles by hand.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { budgetVerdict } from '../../tools/harness/budget.mjs';

const steady = (ms, seconds) => Array.from({ length: Math.round((seconds * 1000) / ms) }, () => ms);

test('a steady 60Hz run passes', () => {
  const v = budgetVerdict(steady(16.7, 20));
  assert.equal(v.pass, true);
  assert.ok(Math.abs(v.p95 - 16.7) < 1e-9);
  assert.equal(v.longFrames, 0);
});

test('a run whose typical frame misses 60Hz fails on p95', () => {
  const v = budgetVerdict(steady(23, 20));
  assert.equal(v.pass, false);
  assert.match(v.reason, /p95/);
});

test('long frames beyond one per minute fail even when p95 is fine', () => {
  const frames = steady(16.7, 30);
  frames[400] = 80; frames[900] = 120;          // two stalls in half a minute
  const v = budgetVerdict(frames);
  assert.equal(v.longFrames, 2);
  assert.equal(v.pass, false);
  assert.match(v.reason, /50ms/);
});

test('a single stall in a minute is tolerated', () => {
  const frames = steady(16.7, 60);
  frames[2000] = 90;
  assert.equal(budgetVerdict(frames).pass, true);
});

test('the first 3 seconds (scene warm-up) are ignored', () => {
  const frames = [400, 250, ...steady(16.7, 20)];   // shader-compile hitch at load
  const v = budgetVerdict(frames);
  assert.equal(v.longFrames, 0);
  assert.equal(v.pass, true);
});

test('too little data is not a pass', () => {
  const v = budgetVerdict(steady(16.7, 2));
  assert.equal(v.pass, false);
  assert.match(v.reason, /not enough/);
});
