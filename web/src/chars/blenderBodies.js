/**
 * Loads and caches the 8 Blender-built character bodies (one GLB per
 * `shell/chars.js` CHARS id — see `web/src/assets/chars/README.md`).
 *
 * `makeCast()` (chars/index.js) needs a SYNCHRONOUS "is this character's
 * body ready" check — it can't await a promise mid-construction without
 * turning every caller of `makeCast()`/`charMesh()` async, which the shell
 * and every minigame currently are not. So loading happens once, up front
 * (`preloadBlenderBodies()`, called during the shell's existing warm-up
 * pass), into a plain synchronous map; `getBlenderTemplate(id)` just reads
 * it. A character requested before its body has finished loading falls
 * back to the toy rig for that instance — safe, just less shiny until the
 * cache is warm, never a crash.
 */

import { loadGLB, ASSET_URLS } from '../assets/index.js';
import { clone as cloneSkinned } from 'three/examples/jsm/utils/SkeletonUtils.js';

export const BLENDER_CHAR_IDS = ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz'];

const templates = new Map(); // id -> { scene, animations }
let preloadStarted = false;

/** Kick off loading all 8 bodies. Safe to call more than once. */
export function preloadBlenderBodies() {
  if (preloadStarted) return;
  preloadStarted = true;
  for (const id of BLENDER_CHAR_IDS) {
    const key = `cast-${id}`;
    if (!ASSET_URLS[key]) continue; // asset not registered - skip, don't throw
    loadGLB(key).then((gltf) => {
      templates.set(id, { scene: gltf.scene, animations: gltf.animations });
    }).catch((e) => {
      console.warn(`Blender body '${id}' failed to load, falling back to the toy rig:`, e);
    });
  }
}

/** Synchronous: null until this character's body has finished loading. */
export function getBlenderTemplate(id) {
  return templates.get(id) || null;
}

export function blenderBodiesReady() {
  return BLENDER_CHAR_IDS.every((id) => templates.has(id));
}

/**
 * A fresh, independently-posable instance of `id`'s body: a cloned scene
 * (SkeletonUtils.clone — a plain Object3D.clone() shares bones between
 * instances, which would make every clone of the same character strike the
 * same pose) plus a new mixer and one action per baked clip.
 */
export function instantiateBlenderBody(id) {
  const tpl = getBlenderTemplate(id);
  if (!tpl) return null;
  const scene = cloneSkinned(tpl.scene);
  return { scene, animations: tpl.animations };
}
