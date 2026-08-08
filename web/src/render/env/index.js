/**
 * The environment kit.  [render agent owns this dir]
 *
 * Reusable stage-set pieces, built procedurally, that every minigame composes
 * instead of inventing a world each. A game says:
 *
 *   const env = ctx.stage.createEnv(ctx.scene);
 *   env.stageSet('arena', { groundY: 0 });          // or pick pieces by hand
 *   env.crowd.cheer(1.4);                           // on a combo milestone
 *
 * ...and gets a lit, composed, beat-reactive set for about eight draw calls.
 *
 * Beat reaction is the point. The stage pulses every registered env on every
 * musical beat (see stage.js), each piece decides what "pulse" means for it,
 * and the pieces stagger against each other so the set breathes as an ensemble
 * rather than flashing as one slab. A game does not have to wire any of that
 * up; if it wants an extra accent it calls `env.pulse(1.4)` itself.
 */

import * as THREE from 'three';
import { makeRng } from '../../core/util.js';
import { makeGround } from './ground.js';
import { makeBackdrop } from './backdrop.js';
import { makeFloaters } from './floaters.js';
import { makeCrowd } from './crowd.js';
import { makeBanners } from './banners.js';
import { makeSpotlights } from './spotlights.js';

export const PRESETS = {
  /** Full show: ground, arches, crowd, bunting, rig, floaters. */
  arena: ['ground', 'backdrop', 'crowd', 'banners', 'spotlights', 'floaters'],
  /** No audience — for games where the action fills the frame. */
  stage: ['ground', 'backdrop', 'spotlights', 'floaters'],
  /** Sky-borne: a lone platform in open air. Title screens, menus. */
  void: ['ground', 'backdrop', 'floaters', 'spotlights'],
  /** Playfield with stands but no overhead clutter. */
  field: ['ground', 'backdrop', 'crowd', 'floaters'],
};

const MAKERS = {
  ground: makeGround,
  backdrop: makeBackdrop,
  crowd: makeCrowd,
  banners: makeBanners,
  spotlights: makeSpotlights,
  floaters: makeFloaters,
};

/**
 * @param {object} deps
 * @param {ReturnType<import('../look/index.js').createLook>} deps.look
 */
export function createEnvKit({ look }) {
  /**
   * @param {THREE.Scene|THREE.Object3D} scene
   * @param {{seed?:number}} [o]
   */
  function createEnv(scene, { seed = 0xba5e } = {}) {
    const rng = makeRng(seed);
    const root = new THREE.Group();
    root.name = 'env:root';
    // The kit lights and shades itself through the look system; the dress pass
    // must not second-guess materials that were already built to spec.
    root.userData.house = true;
    scene.add(root);

    const pieces = [];
    const env = {
      root,
      /** @type {Record<string, any>} */
      pieces: {},
    };

    function add(name, maker, opts) {
      if (env.pieces[name]) return env.pieces[name];
      const piece = maker({ look, rng }, opts);
      piece.name = name;
      root.add(piece.group);
      pieces.push(piece);
      env.pieces[name] = piece;
      env[name] = piece;
      return piece;
    }

    for (const [name, maker] of Object.entries(MAKERS)) {
      env[name === 'ground' ? 'addGround' : 'add' + name[0].toUpperCase() + name.slice(1)] =
        (opts) => add(name, maker, opts);
    }

    /**
     * Compose a whole set in one call.
     * @param {keyof PRESETS} preset
     * @param {{groundY?:number, skipGround?:boolean, scale?:number, per?:object}} [o]
     */
    function stageSet(preset = 'arena', o = {}) {
      const list = PRESETS[preset] || PRESETS.arena;
      const per = o.per || {};
      // The whole set rides at ground height, so a game that floors its play
      // space at y = -1.2 gets stands, bunting and rig aligned to it for free.
      root.position.y = o.groundY ?? 0;
      for (const name of list) {
        if (name === 'ground' && o.skipGround) continue;
        add(name, MAKERS[name], { ...(per[name] || {}) });
      }
      return env;
    }

    /** Beat accent across the whole set. */
    function pulse(strength = 1, beatIndex = 0) {
      for (let i = 0; i < pieces.length; i++) pieces[i].pulse?.(strength, beatIndex);
    }

    function update(dt, beat, time, pal) {
      for (let i = 0; i < pieces.length; i++) pieces[i].update?.(dt, beat, time, pal);
    }

    function dispose() {
      for (const p of pieces) p.dispose?.();
      root.parent?.remove(root);
      pieces.length = 0;
    }

    Object.assign(env, { stageSet, pulse, update, dispose, add: (name, maker, opts) => add(name, maker, opts) });
    return env;
  }

  return { createEnv, PRESETS };
}
