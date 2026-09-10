/**
 * The house look, assembled.  [render agent owns this dir]
 *
 * One object that owns sky, lights, materials, blob shadows, the live palette
 * and the post chain, and applies all of it to whatever scene the stage is
 * currently showing.
 *
 * ------------------------------------------------------------------ adopting
 * A minigame does not have to know any of this exists. `dress(scene)` walks a
 * scene and converts it:
 *
 *   - foreign lit materials  -> house toon materials of the same colour
 *   - big flat ground planes -> the house arena floor
 *   - generic scene lights   -> removed, replaced by the house rig
 *   - flat scene.background  -> the gradient sky dome
 *   - scene.fog              -> palette fog
 *
 * A scene that wants to opt out says so:
 *   scene.userData.look = { autoDress:false }        // leave my materials alone
 *   scene.userData.look = { lights:'keep' }          // I light myself
 *   mesh.userData.keepMaterial = true                // this one object is mine
 *
 * Opting out is the exception. Six games that each invent their own lighting
 * is exactly how a series stops looking like a series.
 */

import * as THREE from 'three';
import { PaletteState, getPalette } from './palette.js';
import { createMaterialSystem } from './materials.js';
import { createSky } from './sky.js';
import { createLightRig } from './lighting.js';
import { createShadowField } from './shadows.js';
import { createPost } from './post.js';
import { arenaTexture } from './textures.js';

const LIT_MATERIALS = new Set([
  'MeshStandardMaterial', 'MeshPhysicalMaterial', 'MeshPhongMaterial',
  'MeshLambertMaterial', 'MeshNormalMaterial', 'MeshMatcapMaterial',
]);

/** Lights the house rig replaces. Spot/Point/RectArea are left alone: those
 *  are always placed for a specific local reason. */
const GENERIC_LIGHTS = new Set(['DirectionalLight', 'HemisphereLight', 'AmbientLight']);

export function createLook({ renderer }) {
  const palette = new PaletteState('shell');
  const materials = createMaterialSystem();
  const sky = createSky();
  const lights = createLightRig();
  const shadows = createShadowField();
  const post = createPost({ renderer });

  const rig = new THREE.Group();
  rig.name = '__houseRig';
  rig.add(sky.mesh, lights.group, shadows.mesh);
  // Tag the whole rig so the dress pass never eats its own lights.
  rig.traverse((o) => { o.userData.house = true; });

  let scene = null;
  let fog = null;
  let dressedCount = -1;
  let dressClock = 0;
  const spawned = new Set(); // materials this system created for a scene
  const _wp = new THREE.Vector3();
  /** Where the current scene's floor is, if it brought one. The stage uses
   *  this to sit the environment kit at the same height. */
  const ground = { found: false, y: 0 };

  // ------------------------------------------------------------------ tiers
  let tier = 'high';
  function detectTier() {
    try {
      const gl = renderer.getContext();
      const dbg = gl.getExtension('WEBGL_debug_renderer_info');
      const name = dbg ? String(gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL)) : '';
      // Software rasterisers can do everything, just slowly: keep the look,
      // drop MSAA and the wide bloom level.
      if (/SwiftShader|llvmpipe|Software|Microsoft Basic/i.test(name)) return 'medium';
      if (/Mali-4|Adreno \(TM\) [23]|PowerVR SGX/i.test(name)) return 'low';
    } catch { /* ignore */ }
    return 'high';
  }
  function setTier(t) {
    tier = t === 'auto' ? detectTier() : t;
    post.setTier(tier);
    // The one real shadow map is a high/medium feature; low keeps blobs only.
    renderer.shadowMap.enabled = tier !== 'low';
    return tier;
  }

  /** See lighting.js — the tight box where the key casts a real shadow. */
  function setShadowFocus(center, radius) {
    lights.setShadowFocus(center, radius);
  }
  setTier('auto');

  // ------------------------------------------------------------------ attach

  function attach(s, camera) {
    scene = s;
    s.add(rig);
    fog = new THREE.Fog(palette.col.fog.getHex(), palette.num.fogNear, palette.num.fogFar);
    dressedCount = -1;
    dressClock = 0;
    nextCheck = 0;
    ground.found = false;
    ground.y = 0;
    apply(camera, 0, 0);
  }

  function detach() {
    if (scene) scene.remove(rig);
    shadows.clear();
    lights.setShadowFocus(null);
    for (const m of spawned) { materials.owned.delete(m); m.dispose(); }
    spawned.clear();
    scene = null;
    fog = null;
  }

  // ------------------------------------------------------------------ dress

  function looksLikeGround(mesh) {
    const g = mesh.geometry;
    if (!g) return false;
    const p = g.parameters;
    const isBigPlane = g.type === 'PlaneGeometry' && p && Math.min(p.width, p.height) >= 10;
    const isBigDisc = (g.type === 'CircleGeometry' || g.type === 'RingGeometry') && p && p.radius >= 6;
    if (!isBigPlane && !isBigDisc) return false;
    // horizontal-ish?
    return Math.abs(Math.abs(mesh.rotation.x) - Math.PI / 2) < 0.35 || Math.abs(mesh.rotation.x) < 0.02;
  }

  function dress(force = false) {
    if (!scene) return;
    const opt = scene.userData.look || {};
    if (opt.autoDress === false && !force) { dressedCount = countObjects(); return; }

    const removeLights = opt.lights !== 'keep';
    const strays = [];

    scene.traverse((o) => {
      if (o.userData.house) return;
      if (o.isLight) {
        if (removeLights && GENERIC_LIGHTS.has(o.type)) strays.push(o);
        return;
      }
      if (!o.isMesh || o.userData.keepMaterial) return;
      const mat = o.material;
      if (Array.isArray(mat)) return;
      if (!mat || mat.userData?.housePatched) return;
      if (!LIT_MATERIALS.has(mat.type)) return;

      let next;
      if (looksLikeGround(o)) {
        next = materials.toon({
          color: palette.spec.ground, map: arenaTexture(), bands: 3,
          rim: 0.25, pulse: 0.05, name: 'houseGround',
        });
        o.userData.houseGround = true;
        o.getWorldPosition(_wp);
        ground.found = true;
        ground.y = _wp.y;
      } else {
        next = materials.upgrade(mat, { pulse: 0.14 });
      }
      spawned.add(next);
      o.material = next;
      mat.dispose();
    });

    for (const l of strays) l.parent?.remove(l);

    // The dome owns the background; a flat clear colour behind it would only
    // ever show through as a seam.
    if (scene.background) scene.background = null;
    if (opt.fog !== false) scene.fog = fog;

    dressedCount = countObjects();
  }

  function countObjects() {
    let n = 0;
    scene.traverse(() => { n++; });
    return n;
  }

  /**
   * Cheap re-dress trigger. A scene is built inside an `await`ed `load()`, so
   * the stage may be rendering it while it is still half-constructed, and
   * games legitimately add objects later (a spawned prop, a character). Rather
   * than demand every author remember to call `redress()`, watch the object
   * count: closely for the first seconds of a scene, then on a slow poll.
   */
  let nextCheck = 0;
  function maybeDress(dt) {
    if (!scene) return;
    dressClock += dt;
    if (dressedCount < 0) { dress(); return; }
    if (dressClock < nextCheck) return;
    nextCheck = dressClock + (dressClock < 4 ? 0.2 : 1.5);
    if (countObjects() !== dressedCount) dress();
  }

  // ----------------------------------------------------------------- update

  function apply(camera, beatPulse, time) {
    materials.update(palette, beatPulse, time);
    lights.update(palette, beatPulse);
    sky.update(palette, camera, beatPulse, time);
    shadows.setPalette(palette);
    post.setPalette(palette);
    if (fog) {
      fog.color.copy(palette.col.fog);
      fog.near = palette.num.fogNear;
      fog.far = palette.num.fogFar;
    }
    // Ground material tracks the palette even mid-cross-fade.
    if (scene) {
      for (const m of spawned) {
        if (m.name === 'houseGround') m.color.copy(palette.col.ground);
      }
    }
  }

  function update(dt, { camera, beatPulse = 0, time = 0 } = {}) {
    palette.update(dt);
    maybeDress(dt);
    apply(camera, beatPulse, time);
    shadows.update();
  }

  function setPalette(name, dur = 0.55) {
    palette.set(name, dur);
    return palette;
  }

  function dispose() {
    detach();
    sky.dispose(); lights.dispose(); shadows.dispose(); post.dispose();
    materials.dispose();
  }

  return {
    palette, materials, sky, lights, shadows, post, rig, ground,
    attach, detach, dress, update, setPalette, setTier, setShadowFocus, dispose,
    get tier() { return tier; },
    get colors() { return palette.col; },
    get spec() { return palette.spec; },
    paletteNamed: getPalette,
  };
}
