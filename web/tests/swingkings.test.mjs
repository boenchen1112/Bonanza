/**
 * Swing Kings tap power: the three hit tiers must mean the three timing
 * grades. A critic round found every hit was a HOME RUN because a smooth
 * power curve handed GREATs 0.9 power; this pins the stepped mapping.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { powerFromTiming } from '../src/games/swingKings/index.js';
import { tierFor } from '../src/games/swingKings/rules.js';
import { WINDOWS_MS } from '../src/core/judge.js';

const pitch = { note: { time: 10 } };
const tierAt = (ms) => tierFor(powerFromTiming(10 + ms / 1000, pitch, null)).id;

test('PERFECT timing (early or late) is a home run', () => {
  for (const ms of [0, 20, -30, WINDOWS_MS.perfect - 0.5, -(WINDOWS_MS.perfect - 0.5)]) assert.equal(tierAt(ms), 'homer', `${ms}ms`);
});

test('GREAT timing is a line drive across the whole band', () => {
  for (const ms of [WINDOWS_MS.perfect + 1, 60, -70, WINDOWS_MS.great - 0.5]) assert.equal(tierAt(ms), 'liner', `${ms}ms`);
});

test('GOOD timing is a bunt', () => {
  for (const ms of [WINDOWS_MS.great + 1, 100, -120, WINDOWS_MS.good - 0.5]) assert.equal(tierAt(ms), 'bunt', `${ms}ms`);
});

test('power never rises as timing gets worse', () => {
  let prev = Infinity;
  for (let ms = 0; ms <= WINDOWS_MS.good + 20; ms += 2) {
    const p = powerFromTiming(10 + ms / 1000, pitch, null);
    assert.ok(p <= prev + 1e-12, `${ms}ms: ${p} > ${prev}`);
    prev = p;
  }
});

test('a pitch without a judged note falls back to its beat time', () => {
  const clock = { timeAt: (b) => b * 0.5 };
  assert.equal(powerFromTiming(4, { targetBeat: 8 }, clock), 1);
});
