/**
 * Characters — the facade minigames use.  [chars agent owns this directory]
 *
 * A minigame should never have to know how a rig is assembled. Three calls
 * cover the whole surface:
 *
 * ```js
 * // load()
 * this.cast  = makeCast({ scene: ctx.scene, players: ctx.players });
 * this.crowd = makeCrowd();  ctx.scene.add(this.crowd.mesh);
 *
 * // update(ctx, dt, beat)
 * this.cast.update(dt, beat);
 * this.crowd.update(dt, beat);
 *
 * // on a verdict
 * this.cast.react(playerIndex, verdict);
 * verdict === 'miss' ? this.crowd.deflate() : this.crowd.hype(0.4);
 * ```
 *
 * Everything is beat-driven, so a minigame that never calls anything but
 * `update(dt, beat)` still gets a cast that dances in time.
 */

import * as THREE from 'three';
import {
  makeCharacter, paletteFor, paletteById, buildFor, drawCallsFor,
  PALETTES, BUILDS, BUILD_IDS, disposeSharedResources,
} from './rig.js';
import { CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE, idle, windup, strike, makePose } from './anim.js';
import { makeCrowd } from './crowd.js';

export {
  makeCharacter, paletteFor, paletteById, buildFor, drawCallsFor,
  PALETTES, BUILDS, BUILD_IDS, disposeSharedResources,
  CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE,
  idle, windup, strike, makePose,
  makeCrowd,
};

/**
 * Build a roster and its animators in one call.
 *
 * @param {object} opts
 * @param {THREE.Object3D} opts.scene   where to add the characters
 * @param {Array} [opts.players]        `ctx.players` — {id,name,palette} each
 * @param {number} [opts.count]         used when `players` is absent
 * @param {number[][]} [opts.positions] explicit [x,y,z] per member
 * @param {number} [opts.spacing]       auto-layout spacing when no positions
 * @param {string[]} [opts.builds]      build id per member; defaults to a
 *                                      rotation, so a roster is never one
 *                                      character in four colours
 * @param {'full'|'lite'} [opts.detail]
 * @param {number} [opts.seed]
 * @param {number} [opts.scale]
 * @param {boolean} [opts.faceCamera]   turn members slightly inward
 */
export function makeCast({
  scene, players = null, count = null, positions = null, spacing = 2.1,
  builds = null, detail = 'full', seed = 0x51e, scale = 1, faceCamera = true,
} = {}) {
  const n = count ?? (players ? players.length : 1);
  const members = [];
  const group = new THREE.Group();
  group.name = 'cast';

  for (let i = 0; i < n; i++) {
    const p = players?.[i] || null;
    const pal = p?.palette !== undefined && p.palette !== null
      ? (typeof p.palette === 'string' ? paletteById(p.palette) : paletteFor(p.palette))
      : paletteFor(i);
    const build = builds?.[i] ?? BUILD_IDS[i % BUILD_IDS.length];
    const char = makeCharacter({
      palette: pal, build, seed: (seed + i * 7919) >>> 0, detail, scale,
      name: p?.name || `P${i + 1}`,
    });
    const pos = positions?.[i] || [(i - (n - 1) / 2) * spacing, 0, 0];
    char.position.set(pos[0], pos[1], pos[2]);
    if (faceCamera && !positions) char.rotation.y = -pos[0] * 0.055;
    group.add(char);
    members.push({
      index: i, id: p?.id ?? i, name: p?.name || `P${i + 1}`,
      char, anim: makeAnimator(char, { seed: (seed + i * 104729) >>> 0 }), palette: pal,
    });
  }

  if (scene) scene.add(group);

  const api = {
    group,
    members,
    get length() { return members.length; },
    get drawCalls() { return members.length * drawCallsFor(detail); },

    get(i) { return members[i] ?? null; },
    anim(i) { return members[i]?.anim ?? null; },
    char(i) { return members[i]?.char ?? null; },

    update(dt, beat) {
      for (let i = 0; i < members.length; i++) members[i].anim.update(dt, beat);
    },

    /** Verdict reaction for one member. */
    react(i, verdict, opts) { return members[i]?.anim.react(verdict, opts) ?? null; },

    /** Put everyone in the same state (`ready` before a note, say). */
    all(state, opts) {
      for (const m of members) m.anim.setState(state, opts);
      return api;
    },

    /** Stagger a state across the roster — a chorus line, not a clone army. */
    ripple(state, opts = {}, stepSeconds = 0.08) {
      members.forEach((m, i) => {
        if (stepSeconds <= 0 || i === 0) { m.anim.setState(state, opts); return; }
        setTimeout(() => m.anim.setState(state, opts), i * stepSeconds * 1000);
      });
      return api;
    },

    dispose() {
      for (const m of members) m.char.dispose();
      group.removeFromParent();
      members.length = 0;
    },
  };
  return api;
}
