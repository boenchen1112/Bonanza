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

import * as THREE from 'three';
import { loadGLB, ASSET_URLS } from '../assets/index.js';
import { clone as cloneSkinned } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { createMaterialSystem } from '../render/look/materials.js';

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
 * For a caller that CAN afford to await (a scene's `load(ctx)` - the shell
 * awaits it already, unlike `makeCast()` itself) rather than accept
 * whatever's cached: wait up to `timeoutMs` for one specific character's
 * body, instead of racing the synchronous `getBlenderTemplate()` check
 * against however long the fetch happens to take. Never rejects and never
 * hangs past the timeout - a slow or failed load still resolves, to
 * whatever `getBlenderTemplate(id)` returns at that point (often still
 * null, which is the normal, safe "use the toy rig this time" outcome).
 */
export function waitForBlenderBody(id, timeoutMs = 2500) {
  preloadBlenderBodies();
  if (getBlenderTemplate(id)) return Promise.resolve(true);
  const key = `cast-${id}`;
  if (!ASSET_URLS[key]) return Promise.resolve(false);
  return Promise.race([
    loadGLB(key).then(() => true, () => false),
    new Promise((resolve) => setTimeout(() => resolve(false), timeoutMs)),
  ]);
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
  dressDesignedBody(scene, id);
  return { scene, animations: tpl.animations };
}

// --------------------------------------------------------- designed bodies
//
// A body built by tools/assets/blender/build-character.py (the approved cast
// sheet) is one skinned mesh split into five role materials — body, trim,
// skin, accent, eye — with face morph targets. At runtime each role gets a
// house toon material in the colour the build baked from castData.js, and
// the whole figure gets one skinned inverted-hull outline, so a character
// costs 6 draw calls (5 roles + outline). Bodies from the older build-cast.py
// (no role materials) are left exactly as they were.

export const ROLES = ['body', 'trim', 'skin', 'accent', 'eye'];

// Own material system, not a scene's: these materials are shared by every
// instance of a character across scenes, so no scene's dress pass may own or
// dispose them (every part is flagged keepMaterial).
const castMaterials = createMaterialSystem();
const roleMaterials = new Map();   // `${id}:${role}` -> material
const hullGeometries = new Map();  // id -> merged outline geometry

let outlineMaterial = null;
function getOutlineMaterial() {
  if (outlineMaterial) return outlineMaterial;
  outlineMaterial = new THREE.ShaderMaterial({
    uniforms: { uColor: { value: new THREE.Color(0x120f2e) }, uThick: { value: 0.03 } },
    vertexShader: /* glsl */`
      uniform float uThick;
      #include <common>
      #include <morphtarget_pars_vertex>
      #include <skinning_pars_vertex>
      void main() {
        #include <beginnormal_vertex>
        #include <morphinstance_vertex>
        #include <morphnormal_vertex>
        #include <skinbase_vertex>
        #include <skinnormal_vertex>
        #include <defaultnormal_vertex>
        #include <begin_vertex>
        #include <morphtarget_vertex>
        #include <skinning_vertex>
        vec4 mv = modelViewMatrix * vec4( transformed, 1.0 );
        vec3 n = normalize( normalMatrix * objectNormal );
        mv.xyz += n * uThick * -mv.z * 0.14;
        gl_Position = projectionMatrix * mv;
      }`,
    // Preprocessor lines must start their own line.
    fragmentShader: /* glsl */`
      uniform vec3 uColor;
      void main() {
        gl_FragColor = vec4( uColor, 1.0 );
        #include <colorspace_fragment>
      }`,
    side: THREE.BackSide,
    toneMapped: false,
    fog: false,
  });
  return outlineMaterial;
}

/** Position, normal, skinning and position morphs only: what the hull needs. */
function hullSource(geo) {
  const g = new THREE.BufferGeometry();
  for (const name of ['position', 'normal', 'skinIndex', 'skinWeight']) g.setAttribute(name, geo.getAttribute(name));
  if (geo.index) g.setIndex(geo.index);
  if (geo.morphAttributes.position) g.morphAttributes.position = geo.morphAttributes.position;
  g.morphTargetsRelative = geo.morphTargetsRelative;
  return g;
}

/**
 * Give a freshly cloned designed body its role materials, one shared set of
 * face morph influences, and its outline. No-op for older bodies.
 * @returns {boolean} whether `scene` is a designed body
 */
export function dressDesignedBody(scene, id) {
  const parts = [];
  scene.traverse((o) => { if (o.isSkinnedMesh && ROLES.includes(o.material?.name)) parts.push(o); });
  if (!parts.length) return false;

  for (const m of parts) {
    const role = m.material.name;
    const key = `${id}:${role}`;
    let mat = roleMaterials.get(key);
    if (!mat) {
      mat = castMaterials.toon({
        color: m.material.color.getHex(), bands: 3, rim: role === 'eye' ? 0.2 : 0.8, pulse: 0.08, name: `cast:${key}`,
      });
      roleMaterials.set(key, mat);
    }
    m.material = mat;
    m.userData.keepMaterial = true;
  }

  // One influences array for every part (and the hull): a face change is one
  // write, and the parts can never disagree about the expression.
  const influences = parts[0].morphTargetInfluences || null;
  if (influences) for (const m of parts) m.morphTargetInfluences = influences;

  let geo = hullGeometries.get(id);
  if (!geo) {
    geo = mergeGeometries(parts.map((p) => hullSource(p.geometry)), false);
    hullGeometries.set(id, geo);
  }
  const first = parts[0];
  const hull = new THREE.SkinnedMesh(geo, getOutlineMaterial());
  hull.name = '__castOutline';
  hull.position.copy(first.position);
  hull.quaternion.copy(first.quaternion);
  hull.scale.copy(first.scale);
  hull.bind(first.skeleton, first.bindMatrix);
  if (influences) {
    hull.morphTargetInfluences = influences;
    hull.morphTargetDictionary = first.morphTargetDictionary;
  }
  hull.frustumCulled = false;
  hull.castShadow = false;
  hull.userData.keepMaterial = true;
  hull.userData.isOutline = true;
  first.parent.add(hull);

  scene.userData.designed = { parts, influences, dictionary: first.morphTargetDictionary || {}, hull };
  return true;
}
