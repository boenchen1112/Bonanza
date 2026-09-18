/**
 * The score. One original track per scene, plus a shared menu bed.
 *
 * Tracks are pure data (see any sibling file): a key, a chord progression, and
 * a set of layers whose steps are rendered by `player.js`. Nothing here is a
 * sample and nothing is fetched — the whole soundtrack is a few kilobytes of
 * structure that becomes audio at schedule time.
 */

import swingKings from './swing-kings.js';
import drumlineDash from './drumline-dash.js';
import bounceBrigade from './bounce-brigade.js';
import chompChorus from './chomp-chorus.js';
import finaleFever from './finale-fever.js';
import title from './title.js';
import results from './results.js';
import { SCENES } from '../../shell/registry.js';

/** @type {Record<string, object>} keyed by track id. */
export const TRACKS = Object.fromEntries(
  [swingKings, drumlineDash, bounceBrigade, chompChorus, finaleFever, title, results]
    .map((t) => [t.id, t])
);

/**
 * Which track a scene should play.
 *
 * Every shell scene shares the title theme on purpose: the menus are one
 * continuous place, and restarting the music on each screen would make them
 * feel like separate apps. The minigames each own their own track, so the id
 * maps straight through.
 */
const SCENE_TRACK = {
  menu: 'title',
  results: 'results',
};

/**
 * Host scenes: the shell's `play` wraps a minigame, and the minigame picked
 * its own track in load(). Their activation must leave the music alone —
 * mapped to "silent", it stopped every game launched from the menus (the
 * harness boots games directly, so only the shell path was ever silent).
 */
export const HOST_SCENES = new Set(['play']);

/**
 * Shell scenes are read off the registry rather than listed here a second
 * time. The hand-kept copy had already drifted — it carried a `podium` that
 * no registry entry matches — and adding a screen meant editing this file,
 * which `ARCHITECTURE.md` rule 5 forbids.
 */
const SHELL_SCENES = new Set(
  SCENES.filter((s) => s.kind === 'shell' && !HOST_SCENES.has(s.id)).map((s) => s.id),
);

/**
 * @param {string} sceneId
 * @returns {string|null} track id, or null if the scene should be silent.
 */
export function trackForScene(sceneId) {
  if (!sceneId) return null;
  if (TRACKS[sceneId]) return sceneId;          // minigames: id === track id
  if (SCENE_TRACK[sceneId]) return SCENE_TRACK[sceneId];
  // Every other shell screen shares the title bed: the menus are one
  // continuous place, and restarting the music per screen would make them
  // feel like separate apps.
  if (SHELL_SCENES.has(sceneId)) return 'title';
  return null;
}

export default TRACKS;
