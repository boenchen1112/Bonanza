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
import { CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE, idle, windup, strike, makePose, sampleClip } from './anim.js';
import { makeCrowd } from './crowd.js';
import { CLIP_FACE } from './anim.js';
import { CLIPS } from './clips.gen.js';
import { BlenderCharacterAnimator } from './blenderAnim.js';
import { BLENDER_CHAR_IDS, getBlenderTemplate, instantiateBlenderBody, preloadBlenderBodies } from './blenderBodies.js';

export {
  /** Baked mocap clips (durations, `contact` frames) for `anim.play()`. */
  CLIPS, CLIP_FACE,
  makeCharacter, paletteFor, paletteById, buildFor, drawCallsFor,
  PALETTES, BUILDS, BUILD_IDS, disposeSharedResources,
  CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE,
  idle, windup, strike, makePose, sampleClip,
  makeCrowd,
  /** Blender-body pipeline: call once, early (shell/chars.js's warm-up
   * already does), so bodies are cached by the time a real cast is built.
   * `isBlenderReady(id)` is for callers that build a character once and
   * hold onto it (a roster preview, a locked-in cast slot) - a toy-rig
   * fallback built before its GLB finished loading never upgrades itself,
   * so the caller polls this and rebuilds via `makeCast()`/`charMesh()`
   * again once it flips true. */
  preloadBlenderBodies,
};

export function isBlenderReady(id) {
  return !!(id && BLENDER_CHAR_IDS.includes(id) && getBlenderTemplate(id));
}

/**
 * Build one cast member's 3D representation: the Blender body for `charId`
 * when it's one of the 8 named cast members AND its GLB has finished
 * loading, otherwise the toy rig (unchanged) — a character requested before
 * its body is warm just gets the toy rig for that instance, no error.
 */
function makeMember({ charId, pal, build, charSeed, animSeed, detail, scale, name }) {
  if (charId && BLENDER_CHAR_IDS.includes(charId) && getBlenderTemplate(charId)) {
    const tpl = instantiateBlenderBody(charId);
    if (tpl) {
      const { scene, animations } = tpl;
      scene.updateMatrixWorld(true);
      const mixer = new THREE.AnimationMixer(scene);
      const actions = Object.fromEntries(animations.map((a) => [a.name, mixer.clipAction(a)]));
      const anim = new BlenderCharacterAnimator(scene, mixer, actions, { seed: animSeed });
      scene.userData.isBlenderBody = true;
      scene.dispose = () => { mixer.stopAllAction(); scene.removeFromParent(); };
      return { char: scene, anim };
    }
  }
  const char = makeCharacter({ palette: pal, build, seed: charSeed, detail, scale, name });
  const anim = makeAnimator(char, { seed: animSeed });
  // Settle the pose before this character is ever rendered — see the
  // longer explanation at the original call site this was lifted from.
  for (let k = 0; k < 8; k++) anim.update(1 / 30, 0);
  return { char, anim };
}

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
  // Off by default: several minigames reach past this facade into the toy
  // rig's own joint API - swingKings/world.js attaches the bat via
  // `char.attach('handR', ...)` and builds a custom helmet from
  // `char.joints`/`char.build`, chompChorus reads `.char.joints` directly -
  // none of which exist on a Blender body. Only a caller that has been
  // checked against its own game's character-internals usage should turn
  // this on; the shell's cosmetic displays (title busts, roster, results
  // podium) have been, minigames have not yet.
  allowBlenderBodies = false,
} = {}) {
  const n = count ?? (players ? players.length : 1);
  const members = [];
  const group = new THREE.Group();
  group.name = 'cast';

  for (let i = 0; i < n; i++) {
    const p = players?.[i] || null;
    // A palette may be an id, an index, or a palette object. Objects used to
    // fall into paletteFor(), which coerced them to index 0 — every shell
    // character rendered in the same orange whatever colour it was given.
    const pp = p?.palette;
    const pal = pp === undefined || pp === null ? paletteFor(i)
      : typeof pp === 'object' ? pp
        : typeof pp === 'string' ? paletteById(pp) : paletteFor(pp);
    const build = builds?.[i] ?? BUILD_IDS[i % BUILD_IDS.length];
    // `char` on a player object is the roster character id (e.g. 'tuff');
    // the shell's own single-character path (charMesh()) instead passes it
    // as `id`, since there `id` already IS the character id - see
    // gamePlayers() vs charMesh() in shell/chars.js.
    const charId = allowBlenderBodies
      ? (typeof p?.char === 'string' ? p.char : (typeof p?.id === 'string' ? p.id : null))
      : null;
    const { char, anim } = makeMember({
      charId, pal, build,
      charSeed: (seed + i * 7919) >>> 0, animSeed: (seed + i * 104729) >>> 0,
      detail, scale, name: p?.name || `P${i + 1}`,
    });
    const pos = positions?.[i] || [(i - (n - 1) / 2) * spacing, 0, 0];
    char.position.set(pos[0], pos[1], pos[2]);
    if (faceCamera && !positions) char.rotation.y = -pos[0] * 0.055;
    group.add(char);
    members.push({
      index: i, id: p?.id ?? i, name: p?.name || `P${i + 1}`,
      char, anim, palette: pal,
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
