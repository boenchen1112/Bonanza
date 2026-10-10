/**
 * Shell routing.  [shell agent owns this file]
 *
 * Every screen the shell can show now has its own registry entry (see
 * `registry.js`), so `goView()` is a direct dispatch to `ctx.go()` — kept as
 * its own function only because `resolveActivation()` (below) is where the
 * real routing contract lives, and every navigation call should go through
 * one name rather than reaching for `ctx.go()` inconsistently.
 *
 * The `*Route()` functions below are the actual DECISIONS — pure, state in
 * and `{view, opts}` out, no `ctx`/DOM — that each scene's input handler used
 * to make inline. Extracted here so the same decision (e.g. "where does a
 * player land leaving a round") isn't hand-copied between `play.js` and
 * `results.js`, and so it's unit-testable without booting a scene.
 */

import { SCENES } from './registry.js';

const registered = (id) => SCENES.some((s) => s.id === id);

/**
 * @param {object} ctx
 * @param {string} view
 */
export function goView(ctx, view, opts = {}) {
  if (!registered(view)) throw new Error(`goView: unregistered view "${view}"`);
  ctx.go(view, opts);
}

/** Route into a minigame — through the play wrapper, so pause/party work. */
export function goPlay(ctx, gameId, opts = {}) {
  goView(ctx, 'play', { ...opts, game: gameId });
}

/** `title.js`'s menu confirm -> the next view. */
export function titleMenuRoute(id) {
  if (id === 'party') return { view: 'roster', opts: { mode: 'party' } };
  if (id === 'free') return { view: 'roster', opts: { mode: 'free' } };
  return { view: 'options', opts: {} };
}

/** `roster.js`'s startRun() -> the next view once a lineup is locked in. */
export function rosterExitRoute(mode) {
  return mode === 'party' ? { view: 'party', opts: {} } : { view: 'freeplay', opts: {} };
}

/**
 * Shared by `play.js`'s pause-menu quit() and `results.js`'s confirm(): where
 * a player leaving a round lands, given how they got there. Mid-party always
 * wins — the party hub is the one consistent "next up"/wrap-up screen.
 * @param {{inParty:boolean, from?:string, gameId?:string}} state
 */
export function exitRoute({ inParty, from, gameId }) {
  if (inParty) return { view: 'party', opts: {} };
  if (from === 'freeplay') return { view: 'freeplay', opts: { game: gameId } };
  return { view: 'title', opts: {} };
}

/** `party.js`'s confirm -> the next view: start the next round, or wrap up. */
export function partyConfirmRoute({ done, gameId }) {
  return done ? { view: 'title', opts: {} } : { view: 'play', opts: { game: gameId, from: 'party' } };
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
