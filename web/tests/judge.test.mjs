/**
 * Judgement tests. `node --test web/tests/`
 *
 * These exist because timing bugs are invisible: the game still runs, it just
 * feels wrong, and by the time anyone notices, five minigames are built on it.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { NoteJudge, verdictFor, rankFor, VERDICT, WINDOWS_MS } from '../src/core/judge.js';

const notes = (times, action = 'a') => times.map((t) => ({ time: t, action }));

test('verdict boundaries are inclusive and symmetric', () => {
  assert.equal(verdictFor(0), VERDICT.PERFECT);
  assert.equal(verdictFor(WINDOWS_MS.perfect), VERDICT.PERFECT);
  assert.equal(verdictFor(-WINDOWS_MS.perfect), VERDICT.PERFECT);
  assert.equal(verdictFor(WINDOWS_MS.perfect + 0.001), VERDICT.GREAT);
  assert.equal(verdictFor(WINDOWS_MS.great), VERDICT.GREAT);
  assert.equal(verdictFor(WINDOWS_MS.good), VERDICT.GOOD);
  assert.equal(verdictFor(WINDOWS_MS.good + 0.001), VERDICT.MISS);
  // Early and late of equal magnitude must judge identically. An asymmetric
  // window is a legitimate design choice but must never happen by accident.
  for (const ms of [10, 41, 43, 81, 83, 127, 129]) {
    assert.equal(verdictFor(ms), verdictFor(-ms), `asymmetry at ${ms}ms`);
  }
});

test('a press claims the NEAREST unjudged note, not the next one', () => {
  // The failure this guards against: player is 90ms late on note 1. A
  // next-in-line matcher gives note 1 away and then note 2 is orphaned, and
  // the whole run cascades. Nearest-match keeps the run alive.
  const j = new NoteJudge().load(notes([1.0, 1.5, 2.0]));
  const r = j.press('a', 1.09);
  assert.equal(r.note.time, 1.0, 'should claim note at 1.0, not 1.5');
  assert.equal(r.verdict, VERDICT.GOOD);
  const r2 = j.press('a', 1.5);
  assert.equal(r2.note.time, 1.5);
  assert.equal(r2.verdict, VERDICT.PERFECT);
});

test('a press nearer the following note claims the following note', () => {
  const j = new NoteJudge().load(notes([1.0, 1.2]));
  const r = j.press('a', 1.16);
  assert.equal(r.note.time, 1.2);
});

test('a press outside every claim window is swallowed, not charged', () => {
  const j = new NoteJudge().load(notes([1.0, 2.0]));
  assert.equal(j.press('a', 1.5), null, 'mash between notes must not burn one');
  const r = j.press('a', 2.0);
  assert.equal(r.verdict, VERDICT.PERFECT, 'the real note is still available');
  assert.equal(j.stats.miss, 0);
});

test('a note is never judged twice', () => {
  const j = new NoteJudge().load(notes([1.0]));
  assert.ok(j.press('a', 1.0));
  assert.equal(j.press('a', 1.01), null);
  assert.equal(j.stats.perfect, 1);
});

test('a wrong-action press does not claim a note', () => {
  const j = new NoteJudge().load(notes([1.0], 'left'));
  assert.equal(j.press('a', 1.0), null);
  assert.ok(j.press('left', 1.0));
});

test('notes expire to MISS only after the full claim window', () => {
  const j = new NoteJudge().load(notes([1.0]));
  j.update(1.0 + WINDOWS_MS.claim / 1000 - 0.001);
  assert.equal(j.stats.miss, 0, 'must still be claimable at the edge');
  j.update(1.0 + WINDOWS_MS.claim / 1000 + 0.001);
  assert.equal(j.stats.miss, 1);
});

test('update() never skips an unjudged note when several expire at once', () => {
  // A tab-switch produces one enormous dt. Every passed note must resolve.
  const j = new NoteJudge().load(notes([1, 2, 3, 4, 5]));
  j.update(99);
  assert.equal(j.stats.miss, 5);
  assert.equal(j.finished, true);
});

test('combo breaks on a miss and maxCombo is retained', () => {
  const j = new NoteJudge().load(notes([1, 2, 3, 4]));
  j.press('a', 1); j.press('a', 2); j.press('a', 3);
  assert.equal(j.stats.combo, 3);
  j.update(99);
  assert.equal(j.stats.combo, 0);
  assert.equal(j.stats.maxCombo, 3);
});

test('calibration offset shifts the whole window, not its width', () => {
  // A player 60ms late on everything, with a +60ms... note the sign: the
  // offset is ADDED to press times, so a player who presses late needs a
  // NEGATIVE offset to be pulled back onto the beat.
  const late = new NoteJudge({ offsetMs: -60 }).load(notes([1.0]));
  const r = late.press('a', 1.06);
  assert.equal(r.verdict, VERDICT.PERFECT);
  assert.ok(Math.abs(r.errMs) < 1e-6);
});

test('accuracy and bias report what a player needs to hear', () => {
  const j = new NoteJudge().load(notes([1, 2, 3, 4]));
  for (const t of [1, 2, 3, 4]) j.press('a', t + 0.02); // consistently 20ms late
  assert.equal(j.stats.perfect, 4);
  assert.ok(Math.abs(j.bias - 20) < 0.001, 'bias must surface the rush/drag');
  assert.equal(j.accuracy, 1);
});

test('rank thresholds: S demands a clean run', () => {
  assert.equal(rankFor(1.0, 0), 'S');
  assert.equal(rankFor(1.0, 1), 'A', 'one miss must cost the top rank');
  assert.equal(rankFor(0.93, 0), 'A');
  assert.equal(rankFor(0.5, 9), 'D');
});

test('an empty chart is accuracy 1, not NaN', () => {
  const j = new NoteJudge().load([]);
  assert.equal(j.accuracy, 1);
  assert.equal(j.bias, 0);
  assert.equal(j.finished, true);
});

test('dense 16th-note charts do not mis-assign under jitter', () => {
  // 16ths at 150bpm = 100ms apart, tighter than the claim window. This is the
  // pathological case for any matcher; every press must land on its own note.
  const step = 0.1;
  const times = Array.from({ length: 32 }, (_, i) => 1 + i * step);
  const j = new NoteJudge().load(notes(times));
  let seed = 1;
  const rand = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff - 0.5);
  for (const t of times) j.press('a', t + rand() * 0.03);
  j.update(99);
  assert.equal(j.stats.miss, 0, 'no note orphaned');
  assert.equal(j.stats.perfect + j.stats.great + j.stats.good, 32);
});
