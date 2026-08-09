/**
 * Scene-routing tests. `node --test web/tests/`
 *
 * `resolveActivation` is the chokepoint that stops a shell scene from calling
 * `ctx.go(gameId)` directly and skipping the `play` wrapper (pause, results) —
 * see the bug this closes in CLAUDE.md's Beat Bash Bonanza section.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveActivation } from '../src/shell/nav.js';

const SCENES = [
  { id: 'title', kind: 'shell' },
  { id: 'select', kind: 'shell' },
  { id: 'play', kind: 'shell' },
  { id: 'chars-demo', kind: 'debug' },
  { id: 'swing-kings', kind: 'game' },
];

test('a game-kind target is auto-wrapped into play', () => {
  const r = resolveActivation('swing-kings', { from: 'select' }, SCENES);
  assert.equal(r.id, 'play');
  assert.equal(r.opts.game, 'swing-kings');
  assert.equal(r.opts.from, 'select');
});

test('a shell-kind target passes through unchanged', () => {
  const r = resolveActivation('select', { view: 'options' }, SCENES);
  assert.equal(r.id, 'select');
  assert.deepEqual(r.opts, { view: 'options' });
});

test('a debug-kind target passes through unchanged', () => {
  const r = resolveActivation('chars-demo', {}, SCENES);
  assert.equal(r.id, 'chars-demo');
});

test('an unregistered id is rejected', () => {
  assert.throws(() => resolveActivation('does-not-exist', {}, SCENES));
});
