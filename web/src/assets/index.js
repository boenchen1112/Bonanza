/**
 * Bundled authored assets (ADR 0003).
 *
 * Every file here is imported through Vite, so it is emitted into the build
 * with a hashed name and fetched same-origin at runtime — the game still
 * needs zero network beyond whatever static server hands it its own files.
 * Never load anything from another origin (CDN, API, font service); the
 * harness aborts and flags any such request.
 *
 * Provenance and licence for each file: the README beside it.
 */

import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import ybotUrl from './chars/ybot.glb?url';

export const ASSET_URLS = {
  /** Mixamo Y Bot: shared 52-bone skeleton, meshes `body`/`joints`, 8 clips. */
  ybot: ybotUrl,
};

const loader = new GLTFLoader();
const cache = new Map();

/**
 * Load a GLB once per session. Callers get the SAME parsed gltf — clone its
 * scene (SkeletonUtils.clone for skinned content) before adding it anywhere.
 * @param {keyof ASSET_URLS | string} key
 * @returns {Promise<import('three/examples/jsm/loaders/GLTFLoader.js').GLTF>}
 */
export function loadGLB(key) {
  const url = ASSET_URLS[key] || key;
  let p = cache.get(url);
  if (!p) {
    p = loader.loadAsync(url);
    // A failed load must be retryable, not cached forever.
    p.catch(() => cache.delete(url));
    cache.set(url, p);
  }
  return p;
}
