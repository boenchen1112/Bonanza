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
  title: 'title',
  menu: 'title',
  select: 'title',
  freeplay: 'title',
  roster: 'title',
  party: 'title',
  results: 'results',
  podium: 'results',
};

/**
 * @param {string} sceneId
 * @returns {string|null} track id, or null if the scene should be silent.
 */
export function trackForScene(sceneId) {
  if (!sceneId) return null;
  if (TRACKS[sceneId]) return sceneId;          // minigames: id === track id
  return SCENE_TRACK[sceneId] ?? null;
}

export default TRACKS;
