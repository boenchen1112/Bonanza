import test from 'node:test';
import assert from 'node:assert/strict';
import { Ring } from '../src/core/util.js';

test('a ring keeps the newest N values, oldest first', () => {
  const r = new Ring(3);
  for (const v of [1, 2, 3, 4, 5]) r.push(v);
  assert.equal(r.length, 3);
  assert.deepEqual(r.toArray(), [3, 4, 5]);
});

test('a partly filled ring returns only what was pushed', () => {
  const r = new Ring(4);
  r.push(7); r.push(8);
  assert.deepEqual(r.toArray(), [7, 8]);
});

test('clear empties the ring without reallocating its capacity', () => {
  const r = new Ring(2);
  r.push(1); r.push(2); r.clear();
  assert.equal(r.length, 0);
  r.push(9);
  assert.deepEqual(r.toArray(), [9]);
  assert.equal(r.capacity, 2);
});
