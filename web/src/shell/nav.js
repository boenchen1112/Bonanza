/**
 * Shell routing.  [shell agent owns this file]
 *
 * The shell needs more screens than the registry currently lists, and the
 * registry is integrator-owned. So navigation goes through one indirection:
 * if a view has its own registered scene id, we route straight to it; if it
 * does not, it is hosted inside `select`, which is a dispatcher.
 *
 * The upshot is that adding
 *     { id: 'roster', kind: 'shell', load: () => import('./roster.js') }
 * to the registry is a pure upgrade — deep links and `__BBB__.goto('roster')`
 * start working, and nothing else changes.
 */

import { SCENES } from './registry.js';

/** Views that live inside `select` until they get their own registry entry. */
export const HOSTED = ['roster', 'freeplay', 'party', 'options', 'play'];

const registered = (id) => SCENES.some((s) => s.id === id);

/**
 * @param {object} ctx
 * @param {'title'|'roster'|'freeplay'|'party'|'options'|'play'|'results'} view
 */
export function goView(ctx, view, opts = {}) {
  if (view === 'title' || view === 'results') { ctx.go(view, opts); return; }
  if (registered(view)) { ctx.go(view, opts); return; }
  ctx.go('select', { ...opts, view });
}

/** Route into a minigame — through the play wrapper, so pause/party work. */
export function goPlay(ctx, gameId, opts = {}) {
  goView(ctx, 'play', { ...opts, game: gameId });
}

/** Read the view this scene should present. */
export function viewOf(ctx, fallback = 'freeplay') {
  const v = ctx.opts?.view;
  return typeof v === 'string' && v ? v : fallback;
}

/**
 * The one routing decision every activation must pass through: a `kind:'game'`
 * target is never activated directly, only ever wrapped in `play` (so pause
 * and results are never skippable), a `kind:'shell'`/`'debug'` target passes
 * through unchanged, and an unregistered id is rejected rather than silently
 * failing later inside `getScene`.
 *
 * Pure — takes the registry's scene list as data, not an import, so it is
 * unit-testable without a DOM/WebGL context.
 *
 * @param {string} id
 * @param {object} opts
 * @param {{id:string, kind:string}[]} scenes
 * @returns {{id:string, opts:object}}
 */
export function resolveActivation(id, opts = {}, scenes = SCENES) {
  const entry = scenes.find((s) => s.id === id);
  if (!entry) throw new Error(`resolveActivation: unregistered scene "${id}"`);
  if (entry.kind === 'game') return { id: 'play', opts: { ...opts, game: id } };
  return { id, opts };
}
