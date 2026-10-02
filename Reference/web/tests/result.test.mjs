import test from 'node:test';
import assert from 'node:assert/strict';
import { roundResult, normaliseResult, checkField, emptyStats, _resetWarnings } from '../src/core/result.js';

test('roundResult fills every key so the results screen can rely on the shape', () => {
  const r = roundResult({ score: 1200.6, accuracy: 0.82, rank: 'A' });
  assert.equal(r.score, 1201);
  assert.equal(r.accuracy, 0.82);
  assert.equal(r.rank, 'A');
  assert.deepEqual(r.stats, emptyStats());
  assert.equal(r.highlights, null);
  assert.equal(r.field, null);
});

test('accuracy is hit quality: out-of-range values are clamped, not passed through', () => {
  _resetWarnings();
  // The real defect this guards: a game feeding race points into accuracy.
  assert.equal(roundResult({ accuracy: 4200 }).accuracy, 1);
  assert.equal(roundResult({ accuracy: -3 }).accuracy, 0);
  assert.equal(roundResult({ accuracy: NaN }).accuracy, 0);
});

test('score keeps the game own scale and is never rescaled', () => {
  // Swing Kings returns raw accumulated points; Drumline returns 0..1000.
  assert.equal(roundResult({ score: 48210 }).score, 48210);
  assert.equal(roundResult({ score: 640 }).score, 640);
});

test('a well-formed field survives; places may be shared for a dead heat', () => {
  const field = [{ id: 'p1', place: 0 }, { id: 'p2', place: 0 }, { id: null, place: 2 }];
  assert.deepEqual(roundResult({ field }).field, field);
  assert.equal(checkField(field).ok, true);
});

test('null field is valid — it means the party simulates the CPU rounds', () => {
  assert.equal(checkField(null).ok, true);
  assert.equal(roundResult({}).field, null);
});

test('a malformed field is dropped rather than scored as a race', () => {
  _resetWarnings();
  assert.equal(checkField([{ id: 'p1', place: 1 }]).ok, false);          // no place 0
  assert.equal(checkField([{ id: 'p1', place: 0 }, { id: 'p2', place: 9 }]).ok, false);
  assert.equal(checkField([{ place: 0 }]).ok, false);                     // no id
  assert.equal(checkField([]).ok, false);
  assert.equal(roundResult({ field: [{ id: 'p1', place: 3 }] }).field, null);
});

test('normaliseResult tolerates a half-built minigame', () => {
  const r = normaliseResult(undefined);
  assert.equal(r.score, 0);
  assert.equal(r.accuracy, 0);
  assert.equal(r.rank, null);
  assert.deepEqual(r.stats, emptyStats());
  assert.equal(r.field, null);
});

test('normaliseResult keeps real values and repairs partial stats', () => {
  const r = normaliseResult({ score: 512, accuracy: 0.4, rank: 'B', stats: { perfect: 3, errors: [1, -2] } });
  assert.equal(r.score, 512);
  assert.equal(r.rank, 'B');
  assert.equal(r.stats.perfect, 3);
  assert.equal(r.stats.miss, 0);
  assert.deepEqual(r.stats.errors, [1, -2]);
});

test('normaliseResult refuses a malformed field the same way roundResult does', () => {
  assert.equal(normaliseResult({ field: [{ id: 'a', place: 7 }] }).field, null);
  assert.equal(normaliseResult({ field: 'nope' }).field, null);
});

test('stats keep each game own extras alongside the six shared keys', () => {
  // Drumline carries place/standings/notes; the results screen reads them.
  const r = roundResult({ stats: { perfect: 2, place: 1, notes: 40 } });
  assert.equal(r.stats.perfect, 2);
  assert.equal(r.stats.miss, 0);
  assert.equal(r.stats.place, 1);
  assert.equal(r.stats.notes, 40);
});
