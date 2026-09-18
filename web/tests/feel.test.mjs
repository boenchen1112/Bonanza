import test from 'node:test';
import assert from 'node:assert/strict';
import { hitstopFor } from '../src/core/feel.js';

test('hitstop ends well before the next unjudged note', () => {
  const notes = [{ time: 1.0, judged: true }, { time: 1.074 }, { time: 2 }];
  const s = hitstopFor(0.098, notes, notes[0]);
  assert.ok(s <= 0.074 * 0.4 + 1e-9, `hitstop ${s} runs into a note 74ms later`);
});

test('hitstop is untouched when the next note is far away or absent', () => {
  const notes = [{ time: 1.0, judged: true }, { time: 2.0 }];
  assert.equal(hitstopFor(0.078, notes, notes[0]), 0.078);
  assert.equal(hitstopFor(0.078, [notes[0]], notes[0]), 0.078);
});

test('chord partners and already-judged notes do not count as the next note', () => {
  const notes = [{ time: 1.0, judged: true }, { time: 1.0 }, { time: 1.05, judged: true }, { time: 1.5 }];
  assert.equal(hitstopFor(0.078, notes, notes[0]), 0.078);
});
