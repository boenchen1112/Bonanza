/**
 * Scene-routing tests. `node --test web/tests/`
 *
 * `resolveActivation` is the chokepoint that stops a shell scene from calling
 * `ctx.go(gameId)` directly and skipping the `play` wrapper (pause, results) —
 * see the bug this closes in CLAUDE.md's Beat Bash Bonanza section.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  resolveActivation, titleMenuRoute, rosterExitRoute, exitRoute, partyConfirmRoute,
} from '../src/shell/nav.js';

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

test('titleMenuRoute maps each menu item to its screen', () => {
  assert.deepEqual(titleMenuRoute('party'), { view: 'roster', opts: { mode: 'party' } });
  assert.deepEqual(titleMenuRoute('free'), { view: 'roster', opts: { mode: 'free' } });
  assert.deepEqual(titleMenuRoute('options'), { view: 'options', opts: {} });
});

test('rosterExitRoute sends a locked-in lineup to the right next screen', () => {
  assert.equal(rosterExitRoute('party').view, 'party');
  assert.equal(rosterExitRoute('free').view, 'freeplay');
});

test('exitRoute: mid-party always wins, regardless of how the round was reached', () => {
  assert.equal(exitRoute({ inParty: true, from: 'freeplay', gameId: 'swing-kings' }).view, 'party');
  assert.equal(exitRoute({ inParty: true, from: 'select', gameId: 'swing-kings' }).view, 'party');
});

test('exitRoute: free play returns to the carousel focused on the game just played', () => {
  const r = exitRoute({ inParty: false, from: 'freeplay', gameId: 'swing-kings' });
  assert.deepEqual(r, { view: 'freeplay', opts: { game: 'swing-kings' } });
});

test('exitRoute: unknown "from" (including the removed select screen) falls back to title', () => {
  // The placeholder `select` scene was removed: freeplay is the real picker
  // and nothing in the flow ever routed into select.
  assert.equal(exitRoute({ inParty: false, from: 'select' }).view, 'title');
  assert.equal(exitRoute({ inParty: false, from: undefined }).view, 'title');
});

test('partyConfirmRoute: not done starts the next round with its game id', () => {
  const r = partyConfirmRoute({ done: false, gameId: 'chomp-chorus' });
  assert.deepEqual(r, { view: 'play', opts: { game: 'chomp-chorus', from: 'party' } });
});

test('partyConfirmRoute: done wraps up to title', () => {
  assert.deepEqual(partyConfirmRoute({ done: true }), { view: 'title', opts: {} });
});
