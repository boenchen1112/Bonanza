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
  makeCharacter, paletteFor, paletteById, buildFor, drawCallsFor, batchBlobShadows, batchCrests,
  PALETTES, BUILDS, BUILD_IDS, disposeSharedResources,
} from './rig.js';
import { CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE, idle, windup, strike, makePose, sampleClip } from './anim.js';
import { makeCrowd } from './crowd.js';
import { CLIP_FACE } from './anim.js';
import { CLIPS } from './clips.gen.js';

// blenderAnim.js/blenderBodies.js are loaded lazily (dynamic import, below)
// rather than statically here. blenderBodies.js reaches assets/index.js,
// which imports a .glb through Vite's `?url` suffix - meaningless outside
// Vite, so a static import would make merely IMPORTING chars/index.js (this
// file is the facade every game and the shell already import) fail under
// plain Node - which is exactly how tests/swingkings.test.mjs consumes
// swingKings/index.js, which imports this file for CLIPS. A dynamic import
// only resolves when actually called, which real gameplay does and a unit
// test importing pure functions never does.
let blenderMods = null;
let blenderModsPromise = null;
function ensureBlenderMods() {
  if (!blenderModsPromise) {
    blenderModsPromise = Promise.all([import('./blenderAnim.js'), import('./blenderBodies.js')])
      .then(([animMod, bodiesMod]) => { blenderMods = { ...animMod, ...bodiesMod }; });
  }
  return blenderModsPromise;
}

export {
  /** Baked mocap clips (durations, `contact` frames) for `anim.play()`. */
  CLIPS, CLIP_FACE,
  makeCharacter, paletteFor, paletteById, buildFor, drawCallsFor, batchBlobShadows, batchCrests,
  PALETTES, BUILDS, BUILD_IDS, disposeSharedResources,
  CharacterAnimator, makeAnimator, STATES, STATE_DEF, VERDICT_POSE,
  idle, windup, strike, makePose, sampleClip,
  makeCrowd,
};

/**
 * The head shell's radius in world units, measured from a designed body's own
 * anchors (`headCentre` -> `headSide`) — what a caller scales a hat, helmet or
 * crown from. Bodies without anchors (the toy rig, older builds) fall back to a
 * fraction of the figure's height.
 */
export function headRadius(char) {
  const centre = char.getJoint?.('headCentre');
  const side = char.getJoint?.('headSide');
  if (centre && side) {
    return centre.getWorldPosition(new THREE.Vector3())
      .distanceTo(side.getWorldPosition(new THREE.Vector3()));
  }
  const box = new THREE.Box3().setFromObject(char);
  return (box.max.y - box.min.y) * 0.09;
}

/** Blender-body pipeline: call once, early (shell/chars.js's warm-up
 * already does), so bodies are cached by the time a real cast is built.
 * Triggers the lazy module load (see ensureBlenderMods above); safe to
 * call from anywhere, including a plain-Node context that never awaits
 * it - the returned promise just never gets awaited there. */
export function preloadBlenderBodies() {
  ensureBlenderMods().then(() => blenderMods.preloadBlenderBodies());
}

/** For callers that build a character once and hold onto it (a roster
 * preview, a locked-in cast slot) - a toy-rig fallback built before its
 * GLB finished loading never upgrades itself, so the caller polls this
 * and rebuilds via `makeCast()`/`charMesh()` again once it flips true.
 * False (not "unknown") until the Blender subsystem itself has loaded -
 * the same safe default as "this character's body isn't ready yet". */
export function isBlenderReady(id) {
  return !!(blenderMods && id && blenderMods.BLENDER_CHAR_IDS.includes(id) && blenderMods.getBlenderTemplate(id));
}

/** For a scene's `load(ctx)`, which the shell already awaits: wait up to
 * a bounded time for one character's body before deciding whether to
 * build the Blender or toy-rig version, instead of racing whatever's
 * cached at construction time (see blenderBodies.js). */
export async function waitForBlenderBody(id, timeoutMs) {
  await ensureBlenderMods();
  return blenderMods.waitForBlenderBody(id, timeoutMs);
}

/**
 * Build one cast member's 3D representation: the Blender body for `charId`
 * when it's one of the 8 named cast members AND its GLB has finished
 * loading, otherwise the toy rig (unchanged) — a character requested before
 * its body is warm just gets the toy rig for that instance, no error.
 */
function makeMember({ charId, pal, build, charSeed, animSeed, detail, scale, name }) {
  if (blenderMods && charId && blenderMods.BLENDER_CHAR_IDS.includes(charId) && blenderMods.getBlenderTemplate(charId)) {
    const tpl = blenderMods.instantiateBlenderBody(charId);
    if (tpl) {
      const { scene, animations } = tpl;
      scene.updateMatrixWorld(true);
      const mixer = new THREE.AnimationMixer(scene);
      const actions = Object.fromEntries(animations.map((a) => [a.name, mixer.clipAction(a)]));
      const anim = new blenderMods.BlenderCharacterAnimator(scene, mixer, actions, { seed: animSeed });
      scene.scale.setScalar(scale);
      scene.userData.isBlenderBody = true;
      scene.userData.anim = anim;
      scene.dispose = () => { mixer.stopAllAction(); scene.removeFromParent(); };

      // Same joint-name vocabulary as the toy rig's char.attach() (rig.js),
      // mapped onto this skeleton's bones - so a caller that already only
      // knows joint names ('handR', 'head', ...), never rig internals,
      // doesn't have to branch on which rig it got. 'face' and 'bobble'
      // have no equivalent (no face joints, no springy appendage on a
      // skinned mesh) and fall back to the head bone as the closest thing.
      const BONE_MAP = {
        root: '', hips: 'mixamorigHips', torso: 'mixamorigSpine2',
        head: 'mixamorigHead', face: 'mixamorigHead', bobble: 'mixamorigHead',
        handL: 'mixamorigLeftHand', handR: 'mixamorigRightHand',
        footL: 'mixamorigLeftFoot', footR: 'mixamorigRightFoot',
      };
      /** Read-only counterpart to attach() - the bone itself, for a caller
       * that needs to measure it (calibration) rather than hang something
       * off it. No toy-rig equivalent exists (use char.joints there). */
      // Designed bodies (build-character.py) carry real anchors on the head
      // bone: 'face' is the centre of the face, 'headTop' the top of the head
      // shell. Older bodies fall back to the head bone for both.
      const ANCHORS = { face: 'face_anchor', headTop: 'head_top', headCentre: 'head_centre', headSide: 'head_side' };
      scene.getJoint = (jointName) => {
        const anchor = ANCHORS[jointName] && scene.getObjectByName(ANCHORS[jointName]);
        if (anchor) return anchor;
        const boneName = jointName === 'headTop' ? BONE_MAP.head : BONE_MAP[jointName];
        return boneName ? scene.getObjectByName(boneName) : (boneName === '' ? scene : null);
      };
      scene.attach = (jointName, obj) => {
        const target = scene.getJoint(jointName);
        if (!target) return null;
        target.add(obj);
        // A bone's own world scale is the character's overall root scale
        // (~0.01, this skeleton's baked cm->m factor, times this build's
        // silhouette scale) - every bone inherits it, since it's an
        // ancestor-chain scale, not something bones reset. Left alone, an
        // attached object's own position/size offsets - authored assuming
        // real-world metres, same as attaching to a toy-rig joint - end up
        // shrunk by that same ~100x, landing sub-pixel. Counter-scale here
        // so a caller's numbers mean the same thing on either rig.
        const s = new THREE.Vector3();
        target.getWorldScale(s);
        obj.scale.set(1 / (s.x || 1), 1 / (s.y || 1), 1 / (s.z || 1));
        return target;
      };
      // Shadow detail: a skinned mesh is 1-2 meshes total (body + optional
      // crest), nowhere near the toy rig's per-limb mesh count, so there is
      // no cheaper "core" tier worth having - always cast the whole thing.
      scene.setShadowDetail = () => { scene.traverse((o) => { if (o.isMesh && !o.userData.isOutline) o.castShadow = true; }); };
      scene.setShadowDetail();
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
